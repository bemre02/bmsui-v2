namespace BmsUi.Model;

public enum PackEventType { FaultRaised, FaultCleared, OutputOn, OutputOff }

public enum EventSeverity { Info, Critical }

/// <summary>
/// One recorded pack event. <see cref="Label"/> is built when the event is emitted so the UI
/// and the exporter render identical text with no shared formatting. <see cref="Duration"/> is
/// set only on <see cref="PackEventType.FaultCleared"/>.
/// </summary>
public readonly record struct PackEvent(
    DateTime At, PackEventType Type, string Label, TimeSpan? Duration, EventSeverity Severity);
