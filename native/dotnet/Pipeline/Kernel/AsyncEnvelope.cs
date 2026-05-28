#nullable enable

using System;
using System.Runtime.CompilerServices;
using MMP.Herald.Events;
using MMP.Herald.Levels;
using MMP.Herald.Templating;

namespace MMP.Herald.Pipeline.Kernel;

// ── Lever A — inline async envelope (default async-handoff payload) ────
//
// Promoted to shipped Core from the soak-test harness at
// Modules/Core/soak-tests/Herald.MultiSourceLoadTest/AsyncEnvelope.cs after
// the paced-regime re-measure characterised the cost honestly (mean −12.22ms
// max-pause on N=3 reps at 24-conn × 100KHz; +100.6% throughput and 296→0.3
// B/event at 96-conn × flat-out). Catalog + rationale:
// docs/_wip/lever-a-paced-remeasure-2026-05-27/async-handoff-design-space-catalog.md.
//
// The envelope carries everything FastPathAsyncSink's drain needs, inline,
// so it can ride Channel<AsyncEnvelope> by value with zero per-event heap
// allocation on the producer (arity ≤ 8) or one overflow array on producers
// with arity > 8. At the drain boundary the consumer reconstitutes either a
// LogEventBuffer on its own stack (IKernelSink inner — truly 0-alloc
// system-wide) or a heap LogEvent (legacy inner — alloc relocated from
// producer to consumer thread).
//
// The producer resolves LogProperty.Lazy factories EAGERLY at envelope
// construction (on the producer thread, while the original AsyncLocal /
// HttpContext / tenant scope is still in effect). The original harness
// did not do this; the eager-resolution discipline is the L1 layer of
// the async-sink cross-tenant PII fix (see #90 / async-sink-cross-tenant-
// pii-posture). Operators who genuinely want deferred resolution opt in
// per-property via explicit pre-wrapping in Lazy<T> before passing to
// LogProperty.Lazy.

// One compact property slot. Lossless: carries the same information a full
// LogProperty record carries, packed into value-type fields so an array of
// these is contiguous and inline. Reconstitutes the CaptureMode / Visibility
// singletons at the drain via lookup off the packed Axes byte.
//
// Cognitive-complexity note: the Axes byte is the only non-obvious field.
// bit0 = Visibility.Silent; bits1-2 = CaptureMode (0 Default, 1 Destructure,
// 2 Stringify). Everything else is a direct field copy.
internal readonly struct EnvelopeSlot
{
    public readonly string Name;
    public readonly object? Value;
    public readonly string? Format;
    public readonly byte Axes;

    private EnvelopeSlot(string name, object? value, string? format, byte axes)
    {
        Name = name;
        Value = value;
        Format = format;
        Axes = axes;
    }

    // Pack a full LogProperty's two axis singletons into one byte. This is
    // the lossless step the existing compact path (Name, Value only) drops.
    private const byte SilentBit = 0b0000_0001;
    private const int  CaptureShift = 1;
    private const byte CaptureMask = 0b0000_0110;

    public static byte PackAxes(in LogProperty p)
    {
        byte axes = 0;
        if (p.VisibilityOrDefault == LogPropertyVisibility.Silent) axes |= SilentBit;
        var capture = p.CaptureModeOrDefault;
        var captureCode =
            capture == LogPropertyCaptureMode.Destructure ? 1 :
            capture == LogPropertyCaptureMode.Stringify   ? 2 : 0;
        axes |= (byte)(captureCode << CaptureShift);
        return axes;
    }

    // Construct a slot from a LogProperty. Lazy factories resolve on the
    // PRODUCER thread per the async-path lazy-resolution contract — the
    // original AsyncLocal / HttpContext / tenant scope still exists here;
    // it does not by the time the drain thread reads the value.
    public static EnvelopeSlot FromLogProperty(in LogProperty p)
    {
        var value = p.Value is Func<object?> factory
            ? ResolveOnProducerThread(factory, p.Name)
            : p.Value;
        return new EnvelopeSlot(p.Name, value, p.Format, PackAxes(in p));
    }

    // Mirror of LogProperty.ResolvedValue's exception fence: a lazy factory
    // that throws produces a descriptive fallback string instead of failing
    // the whole event. Logging must never crash on a bad property.
    private static object? ResolveOnProducerThread(Func<object?> factory, string propertyName)
    {
        try
        {
            return factory();
        }
        catch (Exception ex)
        {
            return $"[Lazy property '{propertyName}' threw {ex.GetType().Name}: {ex.Message}]";
        }
    }

    // Reconstitute the full LogProperty at the drain boundary. Looks the two
    // axis singletons back up off the packed byte — no allocation beyond the
    // LogProperty struct itself (which is a value type).
    public LogProperty ToLogProperty()
    {
        var visibility = (Axes & SilentBit) != 0
            ? LogPropertyVisibility.Silent
            : LogPropertyVisibility.Rendered;
        var captureCode = (Axes & CaptureMask) >> CaptureShift;
        var capture =
            captureCode == 1 ? LogPropertyCaptureMode.Destructure :
            captureCode == 2 ? LogPropertyCaptureMode.Stringify   :
                               LogPropertyCaptureMode.Default;
        return new LogProperty(Name, Value, capture, Format, visibility);
    }
}

// The inline buffer: K=8 pinned (matches LogPropertyKernelBuffer8's
// "8-or-fewer = 99%+ case"). NOT generic-parameterized — fixed at 8 by
// design. Overflow to a heap array only when _count > 8.
[InlineArray(8)]
internal struct EnvelopeSlotBuffer8
{
    private EnvelopeSlot _slot0;
}

// Stack buffer for reconstituting LogProperty[] at the drain boundary without
// a heap allocation. LogProperty is a managed struct (it carries string /
// object? references), so stackalloc is illegal; an [InlineArray] local is
// the supported zero-heap pattern. Mirrors the kernel's LogPropertyKernelBuffer8.
[InlineArray(8)]
internal struct LogPropertyDrainBuffer8
{
    private LogProperty _slot0;
}

// The discriminated-carry envelope. Header scalars + inline slot buffer +
// usually-null overflow. Rides Channel<AsyncEnvelope> by value.
//
// Cognitive-complexity note: the three constructors (kernel-buffer entry,
// heap LogEvent entry, no-op default) duplicate the fill loop on purpose —
// extracting the loop body into a helper would force a non-readonly _inline
// field or unsafe-ref tricks. The duplication keeps each constructor as a
// straight-line sequence: assign scalars, decide count, fill loop, done.
internal readonly struct AsyncEnvelope
{
    // ── Scalar header (reference fields are 8B pointers; structs inline) ──
    public readonly DateTimeOffset TimeUtc;
    public readonly LogLevel Level;
    public readonly LogCategory Category;
    public readonly string MessageTemplate;
    public readonly string Message;
    public readonly LogEventId? EventId;
    public readonly string? GenSource;

    // ── Property carry ──
    private readonly int _count;
    private readonly EnvelopeSlotBuffer8 _inline;
    private readonly EnvelopeSlot[]? _overflow; // non-null ONLY when _count > 8

    public int Count => _count;

    // Construct from a kernel-path buffer. Eager-resolves lazy factories on
    // the producer thread for every slot.
    public AsyncEnvelope(in LogEventBuffer buffer)
    {
        TimeUtc = buffer.TimeUtc;
        Level = buffer.Level;
        Category = buffer.Category;
        MessageTemplate = buffer.MessageTemplate;
        Message = buffer.Message;
        EventId = buffer.EventId;
        GenSource = buffer.GenSource;

        _inline = default;
        _overflow = null;

        // Choose which property surface to read. The two are mutually
        // exclusive by LogEventBuffer construction: full Properties span OR
        // CompactProperties span, never both.
        var fullProps = buffer.Properties;
        if (!fullProps.IsEmpty)
        {
            _count = fullProps.Length;
            if (_count <= 8)
            {
                var inlineSpan = (Span<EnvelopeSlot>)_inline;
                for (var i = 0; i < _count; i++)
                {
                    inlineSpan[i] = EnvelopeSlot.FromLogProperty(fullProps[i]);
                }
            }
            else
            {
                _overflow = new EnvelopeSlot[_count];
                for (var i = 0; i < _count; i++)
                {
                    _overflow[i] = EnvelopeSlot.FromLogProperty(fullProps[i]);
                }
            }
        }
        else if (!buffer.CompactProperties.IsEmpty)
        {
            // Compact slots are by-construction default-axes (HERALD014 enforces
            // this). Inflate each to a LogProperty for the producer-thread
            // eager-resolution pass, then pack into the envelope.
            var compactProps = buffer.CompactProperties;
            _count = compactProps.Length;
            if (_count <= 8)
            {
                var inlineSpan = (Span<EnvelopeSlot>)_inline;
                for (var i = 0; i < _count; i++)
                {
                    var lp = compactProps[i].ToLogProperty();
                    inlineSpan[i] = EnvelopeSlot.FromLogProperty(lp);
                }
            }
            else
            {
                _overflow = new EnvelopeSlot[_count];
                for (var i = 0; i < _count; i++)
                {
                    var lp = compactProps[i].ToLogProperty();
                    _overflow[i] = EnvelopeSlot.FromLogProperty(lp);
                }
            }
        }
        else
        {
            _count = 0;
        }
    }

    // Construct from a heap LogEvent (legacy chain entry). Eager-resolves
    // lazy factories on the producer thread per the async-path contract.
    public AsyncEnvelope(LogEvent logEvent)
    {
        TimeUtc = logEvent.TimeUtc;
        Level = logEvent.Level;
        Category = logEvent.Category;
        MessageTemplate = logEvent.MessageTemplate;
        Message = logEvent.Message;
        EventId = logEvent.EventId;
        GenSource = logEvent.GenSource;

        _inline = default;
        _overflow = null;

        var props = logEvent.Properties;
        _count = props.Count;

        if (_count == 0) return;

        if (_count <= 8)
        {
            var inlineSpan = (Span<EnvelopeSlot>)_inline;
            for (var i = 0; i < _count; i++)
            {
                inlineSpan[i] = EnvelopeSlot.FromLogProperty(props[i]);
            }
        }
        else
        {
            _overflow = new EnvelopeSlot[_count];
            for (var i = 0; i < _count; i++)
            {
                _overflow[i] = EnvelopeSlot.FromLogProperty(props[i]);
            }
        }
    }

    // ── Drain-side reconstitution ──────────────────────────────────────
    //
    // Two reconstitution paths, matching the two inner-sink shapes:
    //   ReconstituteProperties -> LogProperty[] for the heap LogEvent path.
    //   WriteSlotsTo(Span)     -> fill a caller stack span for the buffer
    //                             path (IKernelSink), zero heap.

    // Heap path: build the LogProperty[] the LogEvent record needs. This is
    // the allocation the inline path RELOCATES to the consumer when the inner
    // sink is heap-only.
    public LogProperty[] ReconstituteProperties()
    {
        if (_count == 0) return Array.Empty<LogProperty>();
        var props = new LogProperty[_count];
        if (_overflow is not null)
        {
            for (var i = 0; i < _count; i++) props[i] = _overflow[i].ToLogProperty();
        }
        else
        {
            var inlineSpan = (ReadOnlySpan<EnvelopeSlot>)_inline;
            for (var i = 0; i < _count; i++) props[i] = inlineSpan[i].ToLogProperty();
        }
        return props;
    }

    // Buffer path: fill a caller-provided stack span with reconstituted
    // LogProperty values. No heap. The drain calls this into an [InlineArray]
    // local, builds a LogEventBuffer, and hands it to the IKernelSink —
    // truly 0-alloc system-wide.
    public void WriteSlotsTo(Span<LogProperty> dest)
    {
        if (_overflow is not null)
        {
            for (var i = 0; i < _count; i++) dest[i] = _overflow[i].ToLogProperty();
        }
        else
        {
            var inlineSpan = (ReadOnlySpan<EnvelopeSlot>)_inline;
            for (var i = 0; i < _count; i++) dest[i] = inlineSpan[i].ToLogProperty();
        }
    }
}
