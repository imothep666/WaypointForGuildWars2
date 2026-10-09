using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;


namespace GuildWarsCompanion.Ava.Ui;

/// <summary>
/// A grouped list, flattened into one: each group followed by its own rows.
///
/// MAUI's CollectionView virtualises a grouped list by itself; Avalonia only virtualises a flat
/// one. Material storage is several hundred rows, so the groups are laid out end to end here and
/// the view picks a template by type — the group's header, or the row. Rebuilt whenever the
/// groups change, which is once per refresh.
/// </summary>
public sealed class FlatGroups
{
    private readonly IEnumerable _groups;

    public FlatGroups(INotifyCollectionChanged groups)
    {
        _groups = (IEnumerable)groups;
        groups.CollectionChanged += (_, _) => Rebuild();
        Rebuild();
    }

    public ObservableCollection<object> Items { get; } = [];

    private void Rebuild()
    {
        Items.Clear();

        foreach (var group in _groups)
        {
            Items.Add(group);

            if (group is IEnumerable rows)
                foreach (var row in rows)
                    Items.Add(row);
        }
    }
}
