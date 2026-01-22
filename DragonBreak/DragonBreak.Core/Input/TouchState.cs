#nullable enable
using System;
using System.Collections.Generic;

namespace DragonBreak.Core.Input;

/// <summary>
/// A frame snapshot of multitouch input.
/// </summary>
public sealed class TouchState
{
    public static readonly TouchState Empty = new();

    public IReadOnlyList<TouchPoint> Points => _points;
    private readonly List<TouchPoint> _points = new();

    public bool HasAny => _points.Count > 0;

    public TouchState() { }

    public TouchState(IEnumerable<TouchPoint> points)
    {
        if (points == null) return;
        foreach (var p in points)
            _points.Add(p.Normalize());
    }

    public TouchState Add(TouchPoint p)
    {
        _points.Add(p.Normalize());
        return this;
    }

    public bool TryGetBegan(out TouchPoint began)
    {
        for (int i = 0; i < _points.Count; i++)
        {
            if (_points[i].Phase == TouchPhase.Began)
            {
                began = _points[i];
                return true;
            }
        }

        began = default;
        return false;
    }

    public bool TryGetAnyActive(out TouchPoint active)
    {
        for (int i = 0; i < _points.Count; i++)
        {
            var ph = _points[i].Phase;
            if (ph == TouchPhase.Began || ph == TouchPhase.Moved)
            {
                active = _points[i];
                return true;
            }
        }

        active = default;
        return false;
    }
}

