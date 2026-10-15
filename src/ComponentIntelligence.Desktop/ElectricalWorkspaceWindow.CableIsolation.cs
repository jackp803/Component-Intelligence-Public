using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.Desktop;

public partial class ElectricalWorkspaceWindow
{
    private int AdoptLoadedProject(ElectricalProject loaded)
    {
        var independent = new SchematicCableIsolationService().Separate(loaded);
        var count = independent.Cables.Count - loaded.Cables.Count;
        _project = loaded; _history.Clear(); BeginSession(true);
        if (!ReferenceEquals(loaded, independent))
        {
            RecordMutation("將共用線材拆成獨立實體，保留外部接線");
            _project = independent;
        }
        RefreshAll();
        return count;
    }
}
