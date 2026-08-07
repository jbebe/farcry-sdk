using System.Globalization;
using System.Text;
using JackAll.Tools.Format.Mgb;

namespace JackAll.Core.Tests;

/// <summary>
/// Emits a canonical, line-oriented dump of every corpus package so it can be diffed against the
/// independently-written Python reference implementation
/// (<c>tools/JackAll/src/JackAll.Tools/Format/mgb_parser.py</c>).
/// </summary>
/// <remarks>
/// Round-tripping proves the bytes are reproduced, but not that they are *attributed* correctly:
/// swapping two adjacent fields of the same width round-trips perfectly while labelling both wrong.
/// Comparing decoded values against a separate implementation of the same spec is what catches that.
///
/// Only runs when <c>MGB_DUMP</c> names an output path, so it stays a deliberate verification step
/// rather than a test that writes files on every run.
/// </remarks>
public sealed class MgbDifferentialDumpTests
{
    [Fact]
    public void Dump_corpus_when_requested()
    {
        string? outputPath = Environment.GetEnvironmentVariable("MGB_DUMP");
        if (string.IsNullOrEmpty(outputPath))
        {
            return;
        }

        string corpus = Path.Combine(TestSupport.RepositoryRoot, "tmp", "menu");
        var text = new StringBuilder();
        // Ordinal, not the culture-aware default: the Python reference sorts by raw code point, and
        // '.' vs '_' orders differently under culture rules, which would shuffle whole file blocks.
        foreach (string file in Directory.EnumerateFiles(corpus, "*.mgb").Order(StringComparer.Ordinal))
        {
            MgbPackage package = MgbPackage.Read(File.ReadAllBytes(file));
            text.Append(Dump(Path.GetFileName(file), package));
        }
        File.WriteAllText(outputPath, text.ToString());
    }

    private static string Dump(string name, MgbPackage package)
    {
        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture,
            $"FILE {name} page={package.PageWidth}x{package.PageHeight} " +
            $"off={package.DisplayOffsetX},{package.DisplayOffsetY} " +
            $"mat={package.Materials.Count} fs={package.FontSubsts.Count} " +
            $"fr={package.FontRefs.Count} ff={package.FontFamilies.Count} " +
            $"areas={package.Areas.Count} " +
            $"st={(package.StringTable?.Strings.Count.ToString(CultureInfo.InvariantCulture) ?? "-")} " +
            $"got={(package.GenericObjectTable?.Objects.Count.ToString(CultureInfo.InvariantCulture) ?? "-")} " +
            $"defmat={MgbText.Ansi(package.DefaultMaterialName)}");

        foreach (MgbMaterial material in package.Materials)
        {
            text.AppendLine(CultureInfo.InvariantCulture,
                $"  MAT {material.NameId:X8} {material.TexturePath}");
        }
        foreach (MgbArea area in package.Areas)
        {
            text.AppendLine(CultureInfo.InvariantCulture,
                $"  AREA {area.TypeName} {area.UserData.NameId:X8} props={area.UserData.Properties.Count} " +
                $"fps={area.FrameRate} frame={area.CurrentFrame} elems={area.Elements.Count} " +
                $"box={string.Join(",", area.StaticBox)} " +
                $"act={(area.Action.Executer is { } ax ? ax.TypeName + ":" + ax.Actions.Count : "-")}");
            foreach (MgbElement element in area.Elements)
            {
                DumpElement(text, element);
            }
        }
        return text.ToString();
    }

    private static void DumpElement(StringBuilder text, MgbElement element)
    {
        text.AppendLine(CultureInfo.InvariantCulture,
            $"    EL {element.WidgetTypeName} {element.UserData.NameId:X8} " +
            $"props={element.UserData.Properties.Count} hidden={Bit(element.Hidden)} " +
            $"dup={Bit(element.IsDuplicatable)} mask={element.MaskMode} " +
            $"kf={element.Keyframes.Count} state={element.StateTypeName} " +
            $"act={(element.Action.Executer is { } ax ? ax.TypeName + ":" + ax.Actions.Count : "-")} " +
            $"nb={(element.Focusable is { } f ? f.Neighbors.Count.ToString(CultureInfo.InvariantCulture) : "-")}");

        switch (element.Widget)
        {
            case MgbTextBase { UseStringTable: false } t:
                text.AppendLine(CultureInfo.InvariantCulture, $"      TEXT \"{t.Text}\"");
                break;
            case MgbTextBase { UseStringTable: true } t:
                text.AppendLine(CultureInfo.InvariantCulture,
                    $"      TEXTREF {t.TableId:X8}/{t.ResourceId:X8}");
                break;
            case MgbAreaInstance a:
                text.AppendLine(CultureInfo.InvariantCulture,
                    $"      INST \"{a.LabelText}\" mat={(a.Material.Present ? $"{a.Material.Id:X8}" : "-")} " +
                    $"idx={a.IndexOffset}");
                break;
            case MgbImage i:
                text.AppendLine(CultureInfo.InvariantCulture,
                    $"      IMG mat={(i.Material.Present ? $"{i.Material.Id:X8}" : "-")} " +
                    $"blend={i.BlendingMode} u={i.AddressingModeU} v={i.AddressingModeV}");
                break;
            case MgbRectShape r:
                text.AppendLine(CultureInfo.InvariantCulture,
                    $"      RECT out={Bit(r.IsOutlined)} fill={Bit(r.IsFilled)} blend={r.BlendingMode}");
                break;
        }

        foreach (MgbKeyframe keyframe in element.Keyframes)
        {
            text.AppendLine(CultureInfo.InvariantCulture,
                $"      KF {keyframe.NameId:X8} idx={keyframe.Idx} interp={keyframe.Interpolation} " +
                $"flags={keyframe.State.InterpolationFlags} color={keyframe.State.StateColor:X8}");
        }
    }

    private static string Bit(bool value) => value ? "1" : "0";
}
