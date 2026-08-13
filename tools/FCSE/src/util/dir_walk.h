#pragma once

#include <functional>
#include <string>

namespace FCSE {

// What the walk should do with a directory it just reported.
enum class DirAction {
    Recurse,
    Skip,
};

// Depth-first walk of `directory` (which must end in a backslash). A directory's own files are
// reported before any of its subdirectories are entered, and the search handle is closed before
// recursing rather than held open across the whole tree - so the order plugins and scripts load in
// is stable, and a deep tree does not pin a handle per level.
//
// `onDirectory` decides whether the walk descends, and defaults to descending everywhere; both
// callbacks receive the full path and the bare entry name, and a directory's path keeps its
// trailing backslash.
void WalkDirectory(
    const std::wstring& directory,
    const std::function<void(const std::wstring& fullPath, const std::wstring& name)>& onFile,
    const std::function<DirAction(const std::wstring& fullPath, const std::wstring& name)>&
        onDirectory = nullptr);

// Case-insensitive extension test, since Windows paths are. `dotExtension` includes the dot, and a
// name that is nothing but the extension does not match.
bool HasExtensionI(const std::wstring& name, const wchar_t* dotExtension);

}
