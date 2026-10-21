namespace Retro.Update;

/// <summary>Same-volume, journaled moves. Player data is moved, never deleted or merged blindly.</summary>
public static class UpdateInstaller
{
    public static FileStream Lock(UpdatePlan plan)
    {
        plan.Validate(); Directory.CreateDirectory(UpdatePlan.Metadata(plan.Parent));
        return new FileStream(Path.Combine(UpdatePlan.Metadata(plan.Parent), "apply.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    public static async Task ApplyAsync(UpdatePlan plan, Func<Task> activate, Action<string>? afterMove = null, Func<Task>? deactivate = null)
    {
        plan.Validate();
        if (plan.State != "prepared") throw new InvalidOperationException("Este trabajo no está listo para instalar.");
        var old = PackageManifest.Read(plan.OldRoot); var next = PackageManifest.Read(plan.NewRoot);
        if (old.Version != plan.FromVersion || next.Version != plan.ToVersion || old.Runtime != plan.Runtime || next.Runtime != plan.Runtime)
            throw new InvalidDataException("La instalación cambió desde que se preparó la actualización.");
        old.Verify(plan.OldRoot, exact: false); next.Verify(plan.NewRoot, exact: true);
        // Inspect link boundaries before moving any user data.
        foreach (string path in Directory.EnumerateFileSystemEntries(plan.OldRoot, "*", SearchOption.AllDirectories))
            if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)) throw new IOException("Hay enlaces en la instalación; conservá los datos y actualizá manualmente.");
        plan.State = "applying"; plan.Save();
        try
        {
            Move(plan.OldRoot, plan.Backup);
            foreach (string folder in PackageManifest.ProtectedFolders)
            {
                string previous = Path.Combine(plan.Backup, folder);
                if (!Directory.Exists(previous) && !File.Exists(previous)) continue;
                string target = Path.Combine(plan.NewRoot, folder);
                if (Directory.Exists(target) || File.Exists(target)) Move(target, Path.Combine(plan.Job, "defaults", folder));
                Move(previous, target);
            }
            // Unknown files include portable settings, save states and user documents.
            var own = new HashSet<string>(old.Files.Keys, PackageManifest.Paths) { PackageManifest.FileName };
            foreach (string file in Directory.EnumerateFiles(plan.Backup, "*", SearchOption.AllDirectories).ToArray())
            {
                string relative = Path.GetRelativePath(plan.Backup, file).Replace('\\', '/');
                if (own.Contains(relative)) continue;
                string target = PackageManifest.Within(plan.NewRoot, relative);
                if (File.Exists(target) || Directory.Exists(target)) throw new IOException("Un archivo propio coincide con uno de la nueva entrega: " + relative);
                Move(file, target);
            }
            await activate();
            plan.State = "committed"; plan.Error = null;
            try { plan.Save(); } catch { plan.State = "applying"; throw; }
        }
        catch (Exception error)
        {
            plan.Error = error.Message;
            try { if (deactivate != null) await deactivate(); Rollback(plan); }
            catch (Exception rollback) { plan.State = "recovery-needed"; plan.Error += " / " + rollback.Message; plan.Save(); throw new IOException("La actualización necesita recuperar su diario. Tus archivos se conservan en las dos carpetas.", rollback); }
            throw;
        }

        void Move(string from, string to)
        {
            var move = plan.MoveFor(from, to); plan.ValidateMove(move);
            PackageManifest.NoLinks(Path.GetDirectoryName(from)!); PackageManifest.NoLinks(Path.GetDirectoryName(to)!);
            if (File.Exists(to) || Directory.Exists(to)) throw new IOException("No se sobrescribe un destino existente.");
            plan.Moves.Add(move); plan.Save();
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            if (Directory.Exists(from)) Directory.Move(from, to); else File.Move(from, to);
            afterMove?.Invoke(to);
        }
    }

    public static void Rollback(UpdatePlan plan)
    {
        plan.Validate();
        if (plan.State == "committed") throw new InvalidOperationException("La versión ya se confirmó.");
        if (plan.State == "rolled-back") return;
        plan.RollbackNext ??= plan.Moves.Count - 1; plan.Save();
        while (plan.RollbackNext >= 0)
        {
            var move = plan.Moves[plan.RollbackNext.Value];
            plan.ValidateMove(move); string from = plan.Resolve(move.From), to = plan.Resolve(move.To);
            bool source = File.Exists(from) || Directory.Exists(from), destination = File.Exists(to) || Directory.Exists(to);
            if (!(source && !destination)) // intent saved, move not performed (or already recovered)
            {
                if (source || !destination) throw new IOException("No se puede recuperar un movimiento sin sobrescribir datos.");
                PackageManifest.NoLinks(Path.GetDirectoryName(from)!); PackageManifest.NoLinks(Path.GetDirectoryName(to)!);
                Directory.CreateDirectory(Path.GetDirectoryName(from)!);
                if (Directory.Exists(to)) Directory.Move(to, from); else File.Move(to, from);
            }
            plan.RollbackNext--; plan.Save();
        }
        plan.State = "rolled-back"; plan.Save();
    }
}
