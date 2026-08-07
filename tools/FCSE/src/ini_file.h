#pragma once

#include <string>
#include <vector>

// Order- and comment-preserving INI reader/writer, backing bin\fcse.ini (see settings_registry.h
// for what actually gets stored in it).
//
// "Preserving" is the whole point rather than a nicety: the file is the union of every plugin that
// has *ever* run, but a given launch only sees the plugins currently installed. A plugin that
// isn't loaded this run must not lose its saved settings just because nothing claimed its group,
// so every line FCSE didn't itself write is carried through verbatim, in its original position.
//
// Deliberately not GetPrivateProfileStringW/WritePrivateProfileStringW: that API rewrites the file
// through the OS's own parser, which drops comments and reorders content - exactly the two
// properties this needs to keep.
//
// Format accepted: `[section]` headers, `key = value` pairs (whitespace around both is trimmed),
// and comment lines starting with ';' or '#'. A ';' or '#' *mid-line* is not a comment - it is
// part of the value - so a value can contain either character without quoting.
namespace FCSE {

class IniFile {
public:
    // Reads `path` if it exists. A missing file is not an error: it yields an empty document that
    // Save() will create. Returns false only on a real I/O failure (opened but unreadable).
    bool Load(const std::wstring& path);

    // Rewrites `path` from the in-memory document. Returns false on I/O failure - the caller logs,
    // since it's the one that knows what it was trying to persist.
    bool Save(const std::wstring& path) const;

    // True if the document holds nothing at all - the first-run case, before anything registers.
    bool IsEmpty() const;

    // Appends a comment line to the block above the first [group], to stamp a short explanation
    // into a newly created file. An empty `text` writes a blank line.
    void AddPreambleComment(const std::string& text);

    // The trimmed text stored under [section] key, or nullptr if either is absent.
    const std::string* Find(const std::string& section, const std::string& key) const;

    // Sets [section] key = value, creating either if missing. An existing key keeps its position in
    // the file (only its value text changes); a new key is appended to the end of its section, and
    // a new section to the end of the file.
    void Set(const std::string& section, const std::string& key, const std::string& value);

private:
    struct Line {
        std::string key;   // empty => this line is a comment/blank, reproduced from `raw`
        std::string value; // only meaningful when `key` is non-empty
        std::string raw;   // only meaningful when `key` is empty
    };

    struct Section {
        std::string name; // empty for the implicit section holding any lines before the first
                          // [header] - a leading comment block, typically
        std::vector<Line> lines;
    };

    Section& EnsureSection(const std::string& name);
    const Section* FindSection(const std::string& name) const;

    // Always holds at least the implicit unnamed section, so the accessors above never have to
    // special-case an untouched, never-Load()ed document.
    std::vector<Section> sections_{Section{}};
};

} // namespace FCSE
