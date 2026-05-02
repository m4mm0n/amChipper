using amChipper.Core.Models;

namespace amChipper.App.ViewModels;

/// <summary>
/// Defines the IAutomationLaneViewModel contract.
/// </summary>
public interface IAutomationLaneViewModel
{
    IReadOnlyList<AutomationPoint> Points { get; }
    double PlaybackBeat { get; }
    void BeginHistory(string reason);
    void CommitHistory();
    void CancelHistory();
    void AddPoint(double beat, byte value);
    void DeletePoint(AutomationPoint point);
    void MovePoint(AutomationPoint point, double beat, byte value);
}
