using System.Text.Json;
using P5RArchipelago.Configuration;

namespace P5RArchipelago;

internal sealed partial class ArchipelagoMod
{
    private readonly string _configDirectory;
    private readonly string _catalogIdentity;
    private readonly Config _runtimeConfig;
    private readonly ConnectionSettings _connectionSettings;
    private readonly ConnectionFieldEditor _connectionEditor = new();
    private readonly bool _offlineMenuTest = Environment.GetEnvironmentVariable("P5R_AP_OFFLINE_TEST") == "1";
    private readonly string _offlineTestKey = Guid.NewGuid().ToString("N");
    private int _connectionMenuActive;
    private int _connectionBusy;
    private int _warpMenuChoice;
    private string? _connectionError;
    private readonly Native.ConnectionKeyboard? _connectionKeyboard;

    private void SendCurrentConnectionDeathLink() => _apClient.SendDeathLinkForPartyWipe();

    private ApClient CreateConnectionClient()
    {
        var settings = _connectionSettings;
        var profile = Path.Combine(_configDirectory, "Connections", settings.ProfileKey + ".json");
        if (_offlineMenuTest) profile = Path.Combine(_configDirectory, "OfflineMenuTests", _offlineTestKey + ".json");
        var legacy = Path.Combine(_configDirectory, "State.json");
        if (!File.Exists(profile) && File.Exists(legacy))
        {
            try
            {
                using var data = JsonDocument.Parse(File.ReadAllText(legacy));
                var root = data.RootElement;
                if (root.GetProperty("ServerAddress").GetString() == settings.Host &&
                    root.GetProperty("SlotName").GetString() == settings.Slot &&
                    root.GetProperty("CatalogIdentity").GetString() == _catalogIdentity)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(profile)!);
                    File.Copy(legacy, profile, false);
                    _log.Info("Copied matching legacy AP state into its connection profile; original State.json preserved.");
                }
            }
            catch { _log.Info("No matching legacy AP state was migrated."); }
        }
        var config = new Config { ServerAddress = settings.Host, SlotName = settings.Slot,
            ServerPassword = settings.Password, ConnectOnStartup = settings.AutoConnect,
            HubMenuHotkey = _runtimeConfig.HubMenuHotkey, LogAllChestFlags = _runtimeConfig.LogAllChestFlags,
            ReconcileOpenedChests = _runtimeConfig.ReconcileOpenedChests };
        return new ApClient(config, new StateStore(profile, settings.Host, settings.Slot, _catalogIdentity, _log),
            _catalog, _palaces, _confidants, _personas, _tvBundles, _log);
    }

    private string? GetConnectionMenuText(ushort item)
    {
        // Reserved UI-only IDs must never fall through to out-of-catalog vanilla key items,
        // even when the synthetic CMM bridge failed to initialize. No real inventory uses them.
        return item switch {
            0x4FE0 => Volatile.Read(ref _connectionBusy) != 0 ? "Changing connection..." :
                _connectionError ?? (_apClient.ConnectionReady ? "Connected" :
                    _apClient.ConnectionStatus == "Connected" ? "Disconnected - reconnecting" : _apClient.ConnectionStatus),
            0x4FE1 => _connectionSettings.Host,
            0x4FE2 => _connectionSettings.Slot,
            0x4FE3 => string.IsNullOrEmpty(_connectionSettings.Password) ? "Not set" : "******** (session only)",
            0x4FE4 => _connectionEditor.Display,
            0x4FE5 => _connectionSettings.AutoConnect ? "On" : "Off",
            _ => null
        };
    }

    private int? ConnectionMenuFeature(uint id)
    {
        if (id == 900135) _connectionKeyboard?.End(); // Preserve normal root-close handling below.
        if (id == 900186) return Volatile.Read(ref _hubMenuOpen) != 0 ? 1 : 0;
        if (id == 900187) { _warpMenuChoice = 1; return 1; }
        if (id == 900188) return Interlocked.Exchange(ref _warpMenuChoice, 0);
        if (id == 900189) { _warpMenuChoice = 0; return 1; }
        if (id == 900180) { Volatile.Write(ref _connectionMenuActive, 1); return 1; }
        if (id == 900181) { _connectionKeyboard?.End(); Volatile.Write(ref _connectionMenuActive, 0); return 1; }
        if (id == 900182) return RequestConnectionChange(true);
        if (id == 900183) return RequestConnectionChange(false);
        if (id == 900185)
        {
            _connectionSettings.AutoConnect = !_connectionSettings.AutoConnect;
            SaveConnectionSettings();
            return 1;
        }
        if (id is >= 900191 and <= 900193)
        {
            var field = (int)(id - 900190);
            var value = field == 1 ? _connectionSettings.Host : field == 2 ? _connectionSettings.Slot : _connectionSettings.Password;
            _connectionEditor.Begin(field, value);
            return 1;
        }
        if (id == 900194) { _connectionEditor.Backspace(); return 1; }
        if (id == 900195) { _connectionEditor.Clear(); return 1; }
        if (id == 900196)
        {
            if (Volatile.Read(ref _connectionBusy) != 0) return 0;
            var value = _connectionEditor.Draft;
            var host = _connectionEditor.Field == 1 ? value.Trim() : _connectionSettings.Host;
            var slot = _connectionEditor.Field == 2 ? value : _connectionSettings.Slot;
            if (!ConnectionSettings.IsValid(host, slot)) return 0;
            _connectionSettings.Host = host;
            _connectionSettings.Slot = slot;
            if (_connectionEditor.Field == 3) _connectionSettings.Password = value;
            _connectionError = null;
            SaveConnectionSettings();
            return 1;
        }
        if (id == 900197) return 1; // Draft only: cancel never changes saved fields.
        if (id == 900199) { _connectionKeyboard?.End(); return 1; }
        if (id is >= 960000 and < 964096)
        {
            if (_connectionKeyboard is null) return 0;
            if (!_connectionKeyboard.Begin((int)(id - 960000))) return 0;
            _log.Info("Opened native connection keyboard editor; Enter saves, Escape returns, Delete clears.");
            return 1;
        }
        if (id is >= 940032 and <= 940126)
        {
            var index = ConnectionFieldEditor.Alphabet.IndexOf((char)(id - 940000));
            return _connectionEditor.Append(index) ? 1 : 0;
        }
        return null;
    }

    private void SaveConnectionSettings()
    {
        if (_offlineMenuTest) return;
        try { _connectionSettings.Save(Path.Combine(_configDirectory, "Connection.json")); }
        catch { _log.Error("Could not persist connection preferences; current in-game settings remain in memory."); }
    }

    private int RequestConnectionChange(bool connect)
    {
        if (_offlineMenuTest) { _connectionError = "Offline UI test - networking disabled"; return 0; }
        if (connect && !ConnectionSettings.IsValid(_connectionSettings.Host, _connectionSettings.Slot)) return 0;
        if (Interlocked.CompareExchange(ref _connectionBusy, 1, 0) != 0) return 0;
        _connectionError = null;
        _connectionSettings.AutoConnect = connect;
        SaveConnectionSettings();
        _ = Task.Run(async () => {
            try
            {
                await _apClient.StopAsync();
                if (connect)
                {
                    _apClient = CreateConnectionClient();
                    _bootstrapPreparedForCurrentSession = false;
                    Interlocked.Exchange(ref _confidantShopHintPassAvailable, 1);
                    _apClient.Start();
                }
            }
            catch
            {
                _connectionError = "Connection change failed; try again";
                _log.Error("Could not change the AP connection safely; no credentials were logged.");
            }
            finally { Volatile.Write(ref _connectionBusy, 0); }
        });
        return 1;
    }
}
