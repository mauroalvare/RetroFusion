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
        if (plan.Format == 2) { await ApplyInPlaceAsync(plan, activate, afterMove, deactivate); return; }
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
            if (Directory.Exists(from)) Directory.Move(from, to); else MoveFile(from, to);
            afterMove?.Invoke(to);
        }
    }

    private static async Task ApplyInPlaceAsync(UpdatePlan plan, Func<Task> activate, Action<string>? afterMove, Func<Task>? deactivate)
    {
        plan.Validate();
        if (plan.State != "prepared") throw new InvalidOperationException("Este trabajo no está listo para instalar.");
        var old = PackageManifest.Read(plan.OldRoot); var next = PackageManifest.Read(plan.PayloadRoot);
        if (old.Version != plan.FromVersion || next.Version != plan.ToVersion || old.Runtime != plan.Runtime || next.Runtime != plan.Runtime)
            throw new InvalidDataException("La instalación cambió desde que se preparó la actualización.");
        old.Verify(plan.OldRoot, exact: false); next.Verify(plan.PayloadRoot, exact: true);
        // Inspect without walking through linked directories. No player file is moved.
        var paths = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 };
        foreach (string path in Directory.EnumerateFileSystemEntries(plan.OldRoot, "*", paths))
            if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
                throw new IOException("Hay enlaces en la instalación; no se modificó.");
        var oldNames = new HashSet<string>(old.Files.Keys, PackageManifest.Paths);
        var nextNames = new HashSet<string>(next.Files.Keys, PackageManifest.Paths);
        foreach (string relative in next.Files.Keys.Where(p => !PackageManifest.Protected(p)))
        {
            string target = PackageManifest.Within(plan.OldRoot, relative);
            if (Directory.Exists(target) || File.Exists(target) && !oldNames.Contains(relative))
                throw new IOException("Un archivo propio coincide con uno de la nueva entrega: " + relative);
        }
        PackageManifest.CopyPackage(plan.PayloadRoot, plan.CandidateRoot);
        plan.State = "applying"; plan.Save();
        try
        {
            // Install recovery support first and keep the rest of the program present between replacements.
            foreach (string relative in next.Files.Keys.OrderBy(p => p == "Retro.Update.dll" ? 0 : 1))
            {
                string target = PackageManifest.Within(plan.OldRoot, relative);
                if (PackageManifest.Protected(relative) && (File.Exists(target) || Directory.Exists(target))) continue;
                if (File.Exists(target) && old.Files.TryGetValue(relative, out string? previousHash) &&
                    previousHash.Equals(next.Files[relative], StringComparison.OrdinalIgnoreCase)) continue;
                if (File.Exists(target)) Move(target, PackageManifest.Within(plan.Backup, relative));
                Move(PackageManifest.Within(plan.CandidateRoot, relative), target);
            }
            foreach (string relative in old.Files.Keys.Where(p => !PackageManifest.Protected(p) && !nextNames.Contains(p)))
                Move(PackageManifest.Within(plan.OldRoot, relative), PackageManifest.Within(plan.Backup, relative));
            Move(Path.Combine(plan.OldRoot, PackageManifest.FileName), Path.Combine(plan.Backup, PackageManifest.FileName));
            Move(Path.Combine(plan.CandidateRoot, PackageManifest.FileName), Path.Combine(plan.OldRoot, PackageManifest.FileName));
            next.Verify(plan.OldRoot, exact: false);
            await activate();
            plan.State = "committed"; plan.Error = null;
            try { plan.Save(); } catch { plan.State = "applying"; throw; }
        }
        catch (Exception error)
        {
            try { File.WriteAllText(Path.Combine(plan.Job, "detalle-error.txt"), error.ToString()); }
            catch (Exception diagnostic) when (diagnostic is IOException or UnauthorizedAccessException) { }
            plan.Error = error.Message;
            try { if (deactivate != null) await deactivate(); Rollback(plan); }
            catch (Exception rollback)
            {
                plan.State = "recovery-needed"; plan.Error += " / " + rollback.Message; plan.Save();
                throw new IOException("No se pudo recuperar el programa. Los juegos permanecen en su carpeta; el respaldo está en .retroemu-updates.", rollback);
            }
            throw;
        }

        void Move(string from, string to)
        {
            var move = plan.MoveFor(from, to); plan.ValidateMove(move);
            PackageManifest.NoLinks(Path.GetDirectoryName(from)!); PackageManifest.NoLinks(Path.GetDirectoryName(to)!);
            if (File.Exists(to) || Directory.Exists(to)) throw new IOException("No se sobrescribe un destino existente.");
            plan.Moves.Add(move); plan.Save();
            Directory.CreateDirectory(Path.GetDirectoryName(to)!); MoveFile(from, to);
            afterMove?.Invoke(to);
        }
    }

    private static void MoveFile(string from, string to)
    {
        // Retry short Windows file locks without changing permissions.
        long deadline = System.Diagnostics.Stopwatch.GetTimestamp() + System.Diagnostics.Stopwatch.Frequency * 3;
        while (true)
        {
            try { File.Move(from, to); return; }
            catch (Exception error) when ((error is IOException or UnauthorizedAccessException) &&
                System.Diagnostics.Stopwatch.GetTimestamp() < deadline)
            { Thread.Sleep(50); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { throw new IOException("No se pudo reemplazar " + Path.GetFileName(to) + ": " + error.Message, error); }
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
                if (Directory.Exists(to)) Directory.Move(to, from); else MoveFile(to, from);
            }
            plan.RollbackNext--; plan.Save();
        }
        plan.State = "rolled-back"; plan.Save();
    }
}
