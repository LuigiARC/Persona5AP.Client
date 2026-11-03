using Reloaded.Hooks.Definitions;
using Reloaded.Hooks.Definitions.X64;
using IReloadedHooks = Reloaded.Hooks.ReloadedII.Interfaces.IReloadedHooks;

namespace P5RArchipelago.Native;

/// <summary>
/// Observes P5R's native Persona-stock writer. Common flow function 0xD9
/// (ADD_PERSONA_STOCK) is only one wrapper around this routine; battle negotiation calls the
/// native writer without passing through the flow table. Owner 1 is Joker, the second
/// argument is his selected stock slot, and the third is the Persona ID. Vanilla always runs
/// first so acquisition and Compendium registration remain completely game-owned.
/// </summary>
internal unsafe sealed class PersonaRegistrationMonitor
{
    private const string AddPersonaToStockPattern =
        "48 89 5C 24 08 57 48 83 EC 20 0F B7 C1 48 6B C8 0E 48 0F BF C2 48 03 C8 41 0F B7 D8";
    private const ushort JokerOwnerId = 1;

    [Function(CallingConventions.Microsoft)]
    private delegate nuint AddPersonaToStock(ushort ownerId, short stockSlot, ushort personaId);

    private readonly IHook<AddPersonaToStock> _hook;
    private readonly PersonaCatalog _catalog;
    private readonly Log _log;

    public event Action<PersonaLocation>? PersonaRegistered;

    public PersonaRegistrationMonitor(IReloadedHooks hooks, AddressScanner scanner,
        PersonaCatalog catalog, Log log)
    {
        _catalog = catalog;
        _log = log;

        var function = scanner.FindUnique("AddPersonaToStock", AddPersonaToStockPattern);
        _hook = hooks.CreateHook<AddPersonaToStock>(AddPersonaToStockImpl, function).Activate();
        _log.Info("Resolved native Persona-stock writer; negotiation and flow-script acquisitions are observed after vanilla completes.");
    }

    private nuint AddPersonaToStockImpl(ushort ownerId, short stockSlot, ushort personaId)
    {
        var result = _hook.OriginalFunction(ownerId, stockSlot, personaId);
        if (ownerId != JokerOwnerId || !_catalog.TryGetByPersonaId(personaId, out var persona))
            return result;

        try
        {
            PersonaRegistered?.Invoke(persona);
        }
        catch (Exception ex)
        {
            _log.Error($"Persona registration observer failed safely after vanilla acquisition: {ex.Message}");
        }

        return result;
    }
}
