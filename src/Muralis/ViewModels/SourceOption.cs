using CommunityToolkit.Mvvm.ComponentModel;

namespace Muralis.ViewModels;

/// <summary>
/// Entrée des listes de sources des éditeurs d'écran : l'<see cref="Id"/> est la valeur
/// persistée (sentinelle « LocalFolder »/« LocalFile » ou <c>WallpaperSourceConfig.Id</c>),
/// le <see cref="Label"/> le nom affiché. Observable : renommer une source met à jour les
/// ComboBox en place, sans perturber la sélection (l'instance reste la même).
/// </summary>
public sealed partial class SourceOption(string id, string label) : ObservableObject
{
    public string Id { get; } = id;

    [ObservableProperty]
    private string label = label;

    public override string ToString() => Label;
}
