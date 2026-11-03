using P5RArchipelago.Configuration;
using P5RArchipelago.GameAssets;
using CriFs.V2.Hook.Interfaces;
using Reloaded.Hooks.ReloadedII.Interfaces;
using Reloaded.Mod.Interfaces;
using Reloaded.Mod.Interfaces.Internal;

namespace P5RArchipelago;

public sealed class Startup : IMod
{
    private ArchipelagoMod? _mod;

    public void StartEx(IModLoaderV1 loaderApi, IModConfigV1 modConfigApi)
    {
        var loader = (IModLoader)loaderApi;
        var modConfig = (IModConfig)modConfigApi;
        var log = new Log((ILogger)loader.GetLogger());

        if (loader.GetController<IReloadedHooks>()?.TryGetTarget(out var hooks) != true || hooks is null)
        {
            log.Error("Reloaded hooks are unavailable. Install and enable reloaded.sharedlib.hooks.");
            return;
        }

        var configDirectory = loader.GetModConfigDirectory(modConfig.ModId);

        try
        {
            var importedFiles = LegacyProfileMigration.Import(configDirectory);
            if (importedFiles > 0)
                log.Info($"Imported {importedFiles} existing settings/state files for Persona 5 AP; original profile preserved.");
            var config = Config.LoadOrCreate(Path.Combine(configDirectory, "Config.json"), log.Info);
            if (loader.GetController<ICriFsRedirectorApi>()?.TryGetTarget(out var cri) != true || cri is null)
                throw new InvalidOperationException("The required CRI filesystem loader is unavailable.");
            var modDirectory = loader.GetDirectoryForModId(modConfig.ModId);
            var cpks = cri.GetCpkFilesInGameDir();
            var library = cri.GetCriFsLib();
            log.Info("Preparing Persona 5 AP's local game cache automatically...");
            // Overlap extraction/patching with the remaining loader startup. The bind
            // callback is the initialization barrier, before the game can use these files.
            var preparation = Task.Run(() => GameAssetCache.Prepare(modDirectory, configDirectory, library, cpks));
            var bindFailed = false;
            cri.AddBindCallback(context =>
            {
                if (bindFailed) return;
                try
                {
                    var assets = preparation.GetAwaiter().GetResult();
                    _mod ??= new ArchipelagoMod(hooks, config, configDirectory, log, assets.HubPath);
                    assets.Bind(context, modConfig.ModId);
                    log.Info($"Persona 5 AP ready; {(assets.Reused ? "reused" : "prepared")} local game cache: {assets.DirectoryPath}");
                }
                catch (Exception ex)
                {
                    bindFailed = true;
                    log.Error($"Persona 5 AP could not prepare its game files and was disabled. Original CPKs were not changed. {ex}");
                }
            });
        }
        catch (Exception ex)
        {
            log.Error($"Startup failed safely; no game assets were changed. {ex}");
        }
    }

    public void Suspend() { }
    public void Resume() { }
    public void Unload() { }
    public bool CanUnload() => false;
    public bool CanSuspend() => false;
    public Action Disposing => () => _mod?.Dispose();
}
