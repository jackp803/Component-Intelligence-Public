using System.Windows.Controls;
using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.Desktop;

public partial class SchematicWorkspaceControl
{
    private ContextMenu? PinOrderMenu(SchematicSymbol symbol, SchematicSymbolOwner owner, SchematicAnchor anchor)
    {
        var port = owner.Ports.SingleOrDefault(p => p.Pins.Any(pin => pin.PinId == anchor.EndpointId));
        if (port is null) return null;
        var visual = SchematicAuthoringService.PortPinsInVisualOrder(symbol, port);
        if (visual.Count < 2 || visual.Any(a => a.CadContactId is not null) && !symbol.EndpointDisplayPlacements.Any(p => p.PortId == port.PortId)) return null;
        var ids = visual.Select(a => a.EndpointId).ToArray();
        var index = Array.IndexOf(ids, anchor.EndpointId);
        var menu = new ContextMenu();
        void Add(string title, string icon, bool enabled, Func<string[]> order)
        {
            var item = new MenuItem { Header = title, Icon = new TextBlock { Text = icon }, IsEnabled = enabled && !symbol.Locked };
            item.Click += (_, _) => Apply(p => _service.ReorderPortPins(p, symbol.SymbolId, port.PortId, order()), "已調整 Pin 排列；針號與接法不變");
            menu.Items.Add(item);
        }
        string[] Swap(int target)
        {
            var order = ids.ToArray(); (order[index], order[target]) = (order[target], order[index]); return order;
        }
        Add("往前一個位置", "↑", index > 0, () => Swap(index - 1));
        Add("往後一個位置", "↓", index < ids.Length - 1, () => Swap(index + 1));
        menu.Items.Add(new Separator());
        string[] NumericOrder(bool reverse)
        {
            var pins = port.Pins.Where(p => ids.Contains(p.PinId)).OrderBy(p => int.TryParse(p.PinNumber, out var number) ? number : int.MaxValue)
                .ThenBy(p => p.PinNumber, StringComparer.OrdinalIgnoreCase).Select(p => p.PinId).ToArray();
            return reverse ? pins.Reverse().ToArray() : pins;
        }
        Add("依針號正序", "1↑", true, () => NumericOrder(false));
        Add("依針號反序", "1↓", true, () => NumericOrder(true));
        return menu;
    }
}
