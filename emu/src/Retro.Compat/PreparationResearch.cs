using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Retro.Compat;

/// <summary>A private checkpoint tied to an exact edition; never replaces the player's saved states.</summary>
public sealed record PreparationContinuation(string Machine, string Sha256, byte[] State, IReadOnlyList<string> Boot, int Frames);
public sealed record PreparationExperimentResult(string Signature, string Description, string Outcome, bool Skipped = false);

public static class PreparationResearch
{
    public static string Signature(LabExperiment experiment, byte[] state)
    {
        var inputs = experiment.Steps.Select(s => new
        {
            s.Frame, Keys = string.Join("+", s.Keys.Split('+', StringSplitOptions.TrimEntries)
                .Select(k => k.ToUpperInvariant()).Distinct().Order()), s.Hold,
        }).OrderBy(s => s.Frame).ThenBy(s => s.Keys).ThenBy(s => s.Hold);
        string json = JsonSerializer.Serialize(new { experiment.Kind, experiment.Frames, experiment.RepeatEvery, Inputs = inputs,
            State = Convert.ToHexString(SHA256.HashData(state)) });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    /// <summary>Keep actual play after a tested sequence so later research can observe later events.</summary>
    public static void Advance(Retro.Core.IMachine machine, LabExperiment experiment, CancellationToken ct)
    {
        byte[] before = machine.SaveState();
        var keys = new LabKeys(machine);
        var sink = new short[8192];
        try
        {
            for (int frame = 0; frame < experiment.Frames; frame++)
            {
                ct.ThrowIfCancellationRequested();
                keys.Tick(frame);
                foreach (var step in experiment.DueAt(frame)) keys.Press(step.Keys, step.Hold, frame);
                machine.RunFrame(null); machine.DrainAudio(sink);
            }
            ct.ThrowIfCancellationRequested();
        }
        catch { machine.LoadState(before); throw; }
        finally { keys.ReleaseAll(); machine.ReleaseAllKeys(); }
    }
}
