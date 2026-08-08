#pragma once

// Loads FCSE's own Magma UI package (fcse.mgb) into the running engine.
//
// Why this is the whole ballgame: CUIPageBase::Init resolves a page by hashing its authored name
// and looking that hash up through GenericObjectServer::FindGenericObject. That registry is filled
// by magma::Engine::LoadPackage, which publishes a package's GenericObjectTable as its last step -
// so loading fcse.mgb is exactly what makes the name "FCSE_PAGE" resolvable, and nothing else does.
//
// A private page cannot instead borrow a shipped layout: it would share that layout's magma::Page
// with the stock class that also binds it, and the two screens become one screen. See
// PLAN-own-page.md work item 0.5 for the trail on that.
namespace FCSE {

class MagmaPackage {
public:
    // Loads the package located by PageAssets. Safe to call repeatedly - the work happens once and
    // later calls return the first result. Returns false (logged, never fatal) if the file is
    // missing, the engine isn't up yet, or the engine declines to load it; FCSE must then fall back
    // to its existing behaviour rather than try to display a page that would not resolve.
    static bool Load();

    // True once Load() has succeeded, i.e. once "FCSE_PAGE" resolves.
    static bool Loaded();

    // The magma::Package* the engine handed back, or null. Diagnostics only - FCSE reaches the page
    // through the name registry, not through this pointer.
    static void* Package();
};

} // namespace FCSE
