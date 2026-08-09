namespace JackAll.Tools.Format.Mgb;

/// <summary>One problem <see cref="MgbVerify"/> found, with the path through the package that
/// locates it.</summary>
public sealed record MgbFinding(string Where, string Problem)
{
    public override string ToString() => $"{Where}: {Problem}";
}

/// <summary>What <see cref="MgbVerify.Check"/> found, and how much it looked at.</summary>
/// <remarks>
/// <see cref="ReferencesChecked"/> is reported rather than kept internal because the check's own
/// failure mode is silence: every reference into another package is skipped, so a package whose
/// links all name something unexpected would pass with nothing examined. A low count on a package
/// full of wiring is the signal for that - zero is ordinary for one that only carries fonts.
/// </remarks>
public sealed record MgbVerifyResult(IReadOnlyList<MgbFinding> Findings, int ReferencesChecked)
{
    public bool Ok => Findings.Count == 0;
}

/// <summary>
/// Checks that a package's cross-references resolve.
/// </summary>
/// <remarks>
/// Parsing proves the bytes are well-formed; it says nothing about whether the names line up. Every
/// reference in this format is a CRC32 of a name, and <c>Package::ResolveLinks</c> drops one it
/// cannot find without failing the load - so a <c>FullLink</c> naming an element that does not exist
/// is a perfectly valid package that produces a page with no controls on it. That is the failure
/// this catches.
///
/// Only references the file can answer for itself are checked. A link whose first id is another
/// package's name, an <c>AreaLink</c> into <c>common.mgb</c>, a material owned elsewhere - those are
/// resolved by the engine against whatever else is loaded, so they are skipped rather than guessed
/// at. What that leaves is still the whole of a hand-authored package's own wiring.
/// </remarks>
public static class MgbVerify
{
    /// <summary>Every unresolved reference in <paramref name="package"/>, empty when it holds
    /// together. <paramref name="requiredPages"/> additionally demands a named <c>Page</c> that
    /// <c>CUIPageBase::Init</c> can reach - see <c>RequirePage</c>.</summary>
    /// <param name="package">The package to check.</param>
    /// <param name="requiredPages">Page names the game will ask this package for.</param>
    /// <param name="names">Names to report in rather than raw hashes - what
    /// <see cref="MgbXml.FromXml"/> collected, when the package came from an XML source.</param>
    public static MgbVerifyResult Check(
        MgbPackage package,
        IEnumerable<string>? requiredPages = null,
        IEnumerable<string>? names = null)
    {
        var run = new Run(package, names);
        run.Everything();
        foreach (string page in requiredPages ?? [])
        {
            run.RequirePage(page);
        }
        return new MgbVerifyResult(run.Findings, run.Checked);
    }

    private sealed class Run
    {
        private readonly MgbPackage _package;
        private readonly MgbNameLookup _names;

        /// <summary>The package's own name - the first id of every link into it.</summary>
        private readonly uint _self;

        private readonly Dictionary<uint, MgbArea> _areas = [];
        private readonly Dictionary<MgbArea, Dictionary<uint, MgbElement>> _elements = [];

        public Run(MgbPackage package, IEnumerable<string>? names)
        {
            _package = package;
            _self = package.UserData.NameId;
            _names = MgbNameLookup.For(package);
            foreach (string name in names ?? [])
            {
                _names.Offer(name);
            }
        }

        public List<MgbFinding> Findings { get; } = [];

        /// <summary>References that were this package's own to resolve, and so were.</summary>
        public int Checked { get; private set; }

        public void Everything()
        {
            Index();

            Properties("package", _package.UserData);
            foreach (MgbArea area in _package.Areas)
            {
                string path = $"area {Quote(area.UserData.NameId)}";
                Properties(path, area.UserData);
                Actions(path, area.Action);

                foreach (MgbElement element in area.Elements)
                {
                    string elementPath = $"{path} > element {Quote(element.UserData.NameId)}";
                    Properties(elementPath, element.UserData);
                    Actions(elementPath, element.Action);
                    foreach (MgbKeyframe keyframe in element.Keyframes)
                    {
                        Actions($"{elementPath} > keyframe {Quote(keyframe.NameId)}", keyframe.Action);
                    }
                    Widget(elementPath, element);
                }

                // A page's default-focus tags name elements of the page itself.
                foreach (MgbElementTag tag in area.DefaultElementTags)
                {
                    Element($"{path} DEFAULT_ELEMENT", area, tag.Id);
                }
            }

            foreach (MgbGenericObject entry in _package.GenericObjectTable?.Objects ?? [])
            {
                FullLink($"GENERICOBJECT {Quote(entry.NameId)}", entry.Link);
            }
        }

        /// <summary>
        /// Demands a <c>Page</c> named <paramref name="name"/> that the game can actually reach.
        /// </summary>
        /// <remarks>
        /// <c>CUIPageBase::Init</c> hashes the page name and looks it up through
        /// <c>GenericObjectServer::FindGenericObject</c>, whose registry <c>magma::Engine::LoadPackage</c>
        /// fills from every loaded package's <c>GenericObjectTable</c>. An area not registered there
        /// is authored, laid out, and unreachable.
        /// </remarks>
        public void RequirePage(string name)
        {
            _names.Offer(name);
            uint id = MgbTypeTable.Hash(name);
            string where = $"page \"{name}\"";

            // The name is the registry *key*, not the area's own name - a page can be registered
            // under several, and the stock naming convention means it usually is.
            List<MgbGenericObject> entries = (_package.GenericObjectTable?.Objects ?? [])
                .Where(o => o.NameId == id)
                .ToList();
            if (entries.Count == 0)
            {
                Findings.Add(new MgbFinding(where,
                    "no GenericObjectTable entry is registered under this name, so " +
                    "GenericObjectServer::FindGenericObject cannot resolve it"));
                return;
            }

            Checked++;
            foreach (MgbGenericObject entry in entries)
            {
                if (entry.Link.Ids.Count < 2 || entry.Link.Ids[0] != _self)
                {
                    Findings.Add(new MgbFinding(where,
                        "the registry entry points outside this package, so loading this package alone " +
                        "does not make the name resolvable"));
                }
                else if (_areas.TryGetValue(entry.Link.Ids[1], out MgbArea? area) && area.TypeName != "Page")
                {
                    Findings.Add(new MgbFinding(where,
                        $"the registry entry points at {Quote(entry.Link.Ids[1])}, which is " +
                        $"an {area.TypeName} rather than a Page"));
                }
            }
        }

        // --- indexing -------------------------------------------------------

        private void Index()
        {
            foreach (MgbArea area in _package.Areas)
            {
                if (!_areas.TryAdd(area.UserData.NameId, area))
                {
                    Findings.Add(new MgbFinding($"area {Quote(area.UserData.NameId)}",
                        "a second area shares this name, so links to it resolve to whichever the engine registered first"));
                }

                var byName = new Dictionary<uint, MgbElement>();
                foreach (MgbElement element in area.Elements)
                {
                    // Unnamed elements all hash to 0 and are addressed by nothing, so a repeat of
                    // that is not a collision.
                    if (element.UserData.NameId != 0 && !byName.TryAdd(element.UserData.NameId, element))
                    {
                        Findings.Add(new MgbFinding(
                            $"area {Quote(area.UserData.NameId)} > element {Quote(element.UserData.NameId)}",
                            "two elements of this area share this name"));
                    }
                }
                _elements[area] = byName;
            }
        }

        // --- reference kinds -------------------------------------------------

        private void Properties(string path, MgbUserData data)
        {
            foreach (MgbProperty property in data.Properties)
            {
                if (property.Link is not null)
                {
                    FullLink($"{path} > property {Quote(property.Key)}", property.Link);
                }
            }
        }

        private void Actions(string path, MgbActionCaller caller)
        {
            foreach (MgbAction action in caller.Executer?.Actions ?? [])
            {
                Properties($"{path} > {action.OpcodeName ?? Quote(action.ActionId)}", action.Body);
            }
        }

        private void Widget(string path, MgbElement element)
        {
            switch (element.Widget)
            {
                case MgbImage image:
                    Material($"{path} MATERIALLINK", image.Material);
                    break;
                case MgbTextWidget text:
                    FontFamily($"{path} FONTFAMILY", text.FontFamily);
                    break;
                case MgbAreaInstance instance:
                    Material($"{path} MATERIALLINK", instance.Material);
                    AreaLink($"{path} LINK", instance.Link);
                    break;
                case MgbListBox list:
                    AreaLinks(path, ["HEADERLINK", "ITEMLINK", "FOOTERLINK"], list.Links);
                    break;
                case MgbEditBox edit:
                    AreaLinks(path, ["FIELDLINK", "CURSORLINK"], edit.Links);
                    break;
                case MgbSlider slider:
                    AreaLinks(path, ["TRACKLINK", "KNOBLINK", "HEADERLINK", "FOOTERLINK"], slider.Links);
                    break;
                case MgbWindow window:
                    for (int i = 0; i < window.Sections.Length; i++)
                    {
                        Material($"{path} {MgbWindow.SectionNames[i]}", window.Sections[i].Material);
                    }
                    break;
            }
        }

        /// <summary>
        /// One <c>FullLink</c> chain: package, area, element, then the area that element instances
        /// and a widget inside it.
        /// </summary>
        /// <remarks>
        /// The chain is walked only as far as this package can answer for. Past the element the ids
        /// describe the instanced area's own contents, which usually live in another package - so
        /// the 4th id is checked against the element's own <c>AreaLink</c> (a cheap consistency
        /// check that needs no second file) and the 5th only when that area is local.
        /// </remarks>
        private void FullLink(string where, MgbFullLink link)
        {
            if (link.Ids.Count == 0 || link.Ids[0] != _self)
            {
                return; // an empty link is legal; another package's is not ours to resolve
            }
            if (link.Ids.Count < 2)
            {
                Findings.Add(new MgbFinding(where, "the link names this package and stops, so it resolves to nothing"));
                return;
            }

            Checked++;
            if (!_areas.TryGetValue(link.Ids[1], out MgbArea? area))
            {
                Findings.Add(new MgbFinding(where,
                    $"no area named {Quote(link.Ids[1])} is declared in this package"));
                return;
            }
            if (link.Ids.Count < 3)
            {
                return;
            }

            MgbElement? element = Element(where, area, link.Ids[2]);
            if (element is null || link.Ids.Count < 4)
            {
                return;
            }

            if (element.Widget is not MgbAreaInstance { Link.Area: uint instanced })
            {
                return; // nothing to instance into, so the tail is not this package's to check
            }
            if (link.Ids[3] != instanced)
            {
                Findings.Add(new MgbFinding(where,
                    $"names area {Quote(link.Ids[3])} inside element {Quote(link.Ids[2])}, which " +
                    $"instances {Quote(instanced)}"));
                return;
            }

            // The instanced area is only ours to look inside when it is one of ours.
            MgbAreaLink areaLink = ((MgbAreaInstance)element.Widget).Link!;
            if (link.Ids.Count >= 5 && areaLink.Package == _self && _areas.TryGetValue(instanced, out MgbArea? target))
            {
                Element(where, target, link.Ids[4]);
            }
        }

        private void AreaLinks(string path, string[] names, MgbAreaLink?[] links)
        {
            for (int i = 0; i < links.Length; i++)
            {
                AreaLink($"{path} {names[i]}", links[i]);
            }
        }

        private void AreaLink(string where, MgbAreaLink? link)
        {
            if (link?.Area is not uint area || link.Package != _self)
            {
                return;
            }

            Checked++;
            if (!_areas.ContainsKey(area))
            {
                Findings.Add(new MgbFinding(where,
                    $"names area {Quote(area)} in this package, which declares no such area"));
            }
        }

        private void Material(string where, MgbResourceRef reference)
        {
            if (!IsLocal(reference))
            {
                return;
            }

            Checked++;
            if (_package.Materials.Any(m => m.NameId == reference.Id))
            {
                return;
            }
            Findings.Add(new MgbFinding(where,
                $"names material {Quote(reference.Id)} in this package, which declares no such material"));
        }

        private void FontFamily(string where, MgbResourceRef reference)
        {
            if (!IsLocal(reference))
            {
                return;
            }

            Checked++;
            if (_package.FontFamilies.Any(f => f.NameId == reference.Id))
            {
                return;
            }
            Findings.Add(new MgbFinding(where,
                $"names font family {Quote(reference.Id)} in this package, which declares no such family"));
        }

        /// <summary>An empty <c>PACKAGE</c> means the resource belongs to this package, so it is the
        /// only case a single file can check.</summary>
        private static bool IsLocal(MgbResourceRef reference) =>
            reference.Present && reference.PackageName.Length == 0;

        private MgbElement? Element(string where, MgbArea area, uint id)
        {
            Checked++;
            if (_elements[area].TryGetValue(id, out MgbElement? element))
            {
                return element;
            }
            Findings.Add(new MgbFinding(where,
                $"names element {Quote(id)}, which area {Quote(area.UserData.NameId)} does not contain"));
            return null;
        }

        private string Quote(uint id)
        {
            string name = _names.Describe(id);
            return name.StartsWith('#') ? name : $"\"{name}\"";
        }
    }
}
