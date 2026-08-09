#pragma once

#include <cstddef>

// Hands back the Magma UI package that gives FCSE its own settings page.
//
// The package is embedded in FCSE.exe as an RCDATA resource, so installing the loader is copying
// one file and there is no loose layout that can go missing, get stale, or be replaced with one
// built for a different engine build.
//
// These bytes are FCSE's own to read, not the engine's: nothing in Dunia will open a loose file by
// path either (see magma_package.cpp's note on CFileReaderNomad::Open), so MagmaPackage serves them
// to the engine through a hooked reader. That was already true when the package shipped loose - the
// only thing embedding changes is where the bytes come from.
//
// The package itself is built from tools/FCSE/assets/fcse.mgb.xml - see that folder's README for
// what it contains and why a private page cannot simply borrow a shipped layout.
namespace FCSE {

// A view over the embedded package. Points into FCSE.exe's own image, so it is valid for the
// process lifetime with nothing to free.
struct PackageBytes {
    const unsigned char* data = nullptr;
    size_t size = 0;

    explicit operator bool() const { return data != nullptr && size != 0; }
};

class PageAssets {
public:
    // Resolves the layout matching the game's current aspect and checks it is really a Magma
    // package this engine build would accept. Logs the specific reason and returns an empty view on
    // any failure - FCSE must then fall back to its existing behaviour, never proceed to load a
    // page that would display nothing.
    //
    // The UI ships as two sets, `pc` and `pcwidescreen`, whose pages differ in size and geometry
    // (1024x768 vs 1280x800, and the nav list sits at a different x). FCSE embeds one package built
    // from each and picks the same way the engine does - see the note on the widescreen flag in
    // page_assets.cpp.
    //
    // Safe to call repeatedly; the work happens once and later calls return the first result.
    static PackageBytes Locate();
};

} // namespace FCSE
