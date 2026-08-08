#pragma once

#include <string>

// Locates the Magma UI package that gives FCSE its own settings page, and hands back an absolute
// path the engine can open.
//
// Why absolute: Dunia's generic resolver (FUN_102358a0) checks whether a path is already absolute -
// a ':' or a leading '\\' - before it consults the mounted-archive search chain, and an absolute
// path drops straight through to CreateFileW. Far Cry 2 has no loose-file override for *relative*
// paths, so this is the one route that needs no hook at all. See
// docs/docs/file-formats/archives-fat-dat.md, "The generic resolver".
//
// The package itself is built from tools/FCSE/assets/fcse.mgb.xml - see that folder's README for
// what it contains and why a private page cannot simply borrow a shipped layout.
namespace FCSE {

class PageAssets {
public:
    // Resolves the layout matching the game's current aspect and checks it is really a Magma
    // package this engine build would accept. Logs the specific reason and returns false on any
    // failure - a missing or wrong-version package must make FCSE fall back to its existing
    // behaviour, never proceed to load a page that would display nothing.
    //
    // The UI ships as two sets, `pc` and `pcwidescreen`, whose pages differ in size and geometry
    // (1024x768 vs 1280x800, and the nav list sits at a different x). FCSE ships one package built
    // from each and picks the same way the engine does - see the note on the widescreen flag in
    // page_assets.cpp.
    //
    // Safe to call repeatedly; the work happens once and later calls return the first result.
    static bool Locate();

    // True once Locate() has succeeded.
    static bool Available();

    // Absolute path to fcse.mgb, as ANSI - magma::CFileNameNomad::SetIdentifier takes a
    // `char const*`. Empty unless Available(). Locate() refuses a path that cannot survive the
    // narrowing rather than handing the engine a mangled one.
    static const std::string& PackagePath();

    // The same path as UTF-16, for logging and any Win32 use.
    static const std::wstring& PackagePathWide();
};

} // namespace FCSE
