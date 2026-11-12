using Reloaded.Hooks.Definitions;
using Reloaded.Hooks.Definitions.X64;
using IReloadedHooks = Reloaded.Hooks.ReloadedII.Interfaces.IReloadedHooks;

namespace P5RArchipelago.Native;

/// <summary>
/// Observes both native stock acquisition and Compendium registration. Velvet Room
/// operations can copy complete Persona records without calling the ID-based stock writer.
/// Vanilla runs first; this observer never creates or modifies native Persona records.
/// </summary>
internal unsafe sealed class PersonaRegistrationMonitor
{
    private const string AddPersonaToStockPattern =
        "48 89 5C 24 08 57 48 83 EC 20 0F B7 C1 48 6B C8 0E 48 0F BF C2 48 03 C8 41 0F B7 D8";
    private const string RegisterPersonaPattern =
        "48 89 74 24 10 57 48 83 EC 20 48 8B F9 48 8D 35 ?? ?? ?? ?? 0F B7 49 02 48 6B C1 0E F6 84 30";
    private const ushort JokerOwnerId = 1;
    private const int PersonaRecordSize = 0x30;

    [Function(CallingConventions.Microsoft)]
    private delegate nuint AddPersonaToStock(ushort ownerId, short stockSlot, ushort personaId);

    [Function(CallingConventions.Microsoft)]
    private delegate void RegisterPersona(nuint personaRecord);

    private readonly IHook<AddPersonaToStock> _hook;
    private readonly IHook<RegisterPersona> _registrationHook;
    private readonly byte* _compendium;
    private readonly PersonaCatalog _catalog;
    private readonly Log _log;

    public event Action<PersonaLocation>? PersonaRegistered;

    public PersonaRegistrationMonitor(IReloadedHooks hooks, AddressScanner scanner,
        PersonaCatalog catalog, Log log)
    {
        _catalog = catalog;
        _log = log;

        var function = scanner.FindUnique("AddPersonaToStock", AddPersonaToStockPattern);
        var registration = scanner.FindUnique("RegisterPersonaInCompendium", RegisterPersonaPattern);
        // The stock writer's RIP-relative LEA addresses the 0x30-byte Compendium records.
        _compendium = (byte*)AddressScanner.ResolveRipRelative(function + 0x44, 3, 7);
        _registrationHook = hooks.CreateHook<RegisterPersona>(RegisterPersonaImpl, registration).Activate();
        _hook = hooks.CreateHook<AddPersonaToStock>(AddPersonaToStockImpl, function).Activate();
        _log.Info("Resolved native Persona-stock and Compendium writers; negotiation, scripts and Velvet Room registrations are observed after vanilla completes.");
    }

    private nuint AddPersonaToStockImpl(ushort ownerId, short stockSlot, ushort personaId)
    {
        var result = _hook.OriginalFunction(ownerId, stockSlot, personaId);
        if (ownerId == JokerOwnerId)
            ReportPersona(personaId);
        return result;
    }

    private void RegisterPersonaImpl(nuint personaRecord)
    {
        _registrationHook.OriginalFunction(personaRecord);
        if (personaRecord == 0) return;

        var personaId = *(ushort*)(personaRecord + 2);
        if (!_catalog.TryGetByPersonaId(personaId, out _)) return;

        // Native registration can reject non-registerable records. Only report a
        // completed write with an occupied record for this exact Persona ID.
        var registered = _compendium + personaId * PersonaRecordSize;
        if ((*(ushort*)registered & 1) != 0 && *(ushort*)(registered + 2) == personaId)
            ReportPersona(personaId);
    }

    private void ReportPersona(ushort personaId)
    {
        if (!_catalog.TryGetByPersonaId(personaId, out var persona)) return;
        try
        {
            PersonaRegistered?.Invoke(persona);
        }
        catch (Exception ex)
        {
            _log.Error($"Persona registration observer failed safely after vanilla acquisition: {ex.Message}");
        }
    }
}
