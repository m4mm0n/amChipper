using amChipper.Core.Models;

namespace amChipper.Core.Interfaces;

/// <summary>
/// Contract for a module file player (libopenmpt back-end).
/// Handles loading and rendering tracker modules accepted by libopenmpt.
/// </summary>
public interface IModulePlayer : IDisposable
{
    bool IsLoaded { get; }
    int OrderCount { get; }
    int PatternCount { get; }
    int CurrentOrder { get; }
    int CurrentRow { get; }
    double DurationSecs { get; }
    double PositionSecs { get; set; }
    int CurrentTempo { get; }
    int CurrentSpeed { get; }
    int RestartOrder { get; }
    bool LoopEnabled { get; set; }
    bool LoopFromRestartOrder { get; set; }
    ModuleFormat Format { get; }

    string Title { get; }
    string Artist { get; }
    string Comment { get; }

    /// <summary>Load a module file from a byte array.</summary>
    bool Load(byte[] data, string? fileName = null);

    /// <summary>Render the next chunk of audio into the supplied stereo float buffer.</summary>
    int Render(float[] buffer, int frameCount);

    /// <summary>Seek to a specific order / row in the order list.</summary>
    void SeekToOrder(int order, int row = 0);

    double GetCurrentChannelVuMono(int channel);

    /// <summary>Convert the currently-loaded module into an in-memory Song model (best-effort).</summary>
    Song? ImportAsSong();

    event EventHandler<RowChangedEventArgs>? RowChanged;
    event EventHandler<OrderChangedEventArgs>? OrderChanged;
}

/// <summary>
/// Represents the RowChangedEventArgs component.
/// </summary>
public sealed class RowChangedEventArgs(int order, int pattern, int row) : EventArgs
{
    /// <summary>
    /// Stores or exposes Order.
    /// </summary>
    public int Order { get; } = order;
    /// <summary>
    /// Stores or exposes Pattern.
    /// </summary>
    public int Pattern { get; } = pattern;
    /// <summary>
    /// Stores or exposes Row.
    /// </summary>
    public int Row { get; } = row;
}

/// <summary>
/// Represents the OrderChangedEventArgs component.
/// </summary>
public sealed class OrderChangedEventArgs(int order) : EventArgs
{
    /// <summary>
    /// Stores or exposes Order.
    /// </summary>
    public int Order { get; } = order;
}
