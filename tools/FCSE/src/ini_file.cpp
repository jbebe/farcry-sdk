#include "ini_file.h"

#include <windows.h>

namespace FCSE {

namespace {
    std::string Trim(const std::string& text) {
        size_t begin = text.find_first_not_of(" \t");
        if (begin == std::string::npos) {
            return std::string();
        }
        size_t end = text.find_last_not_of(" \t");
        return text.substr(begin, end - begin + 1);
    }

    // Splits on '\n' and drops a trailing '\r', so both CRLF and LF files read identically. The
    // final line is returned whether or not the file ends with a newline.
    std::vector<std::string> SplitLines(const std::string& text) {
        std::vector<std::string> lines;
        size_t start = 0;
        while (start <= text.size()) {
            size_t end = text.find('\n', start);
            if (end == std::string::npos) {
                if (start < text.size()) {
                    lines.push_back(text.substr(start));
                }
                break;
            }
            std::string line = text.substr(start, end - start);
            if (!line.empty() && line.back() == '\r') {
                line.pop_back();
            }
            lines.push_back(std::move(line));
            start = end + 1;
        }
        return lines;
    }

    bool ReadWholeFile(const std::wstring& path, std::string* out, bool* missing) {
        *missing = false;
        HANDLE file = CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr,
                                   OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (file == INVALID_HANDLE_VALUE) {
            *missing = (GetLastError() == ERROR_FILE_NOT_FOUND ||
                        GetLastError() == ERROR_PATH_NOT_FOUND);
            return false;
        }

        LARGE_INTEGER size{};
        if (!GetFileSizeEx(file, &size) || size.QuadPart > 8 * 1024 * 1024) {
            // 8 MB is far past any plausible config; refusing is better than trying to buffer a
            // file that clearly isn't one.
            CloseHandle(file);
            return false;
        }

        out->resize(static_cast<size_t>(size.QuadPart));
        bool ok = true;
        if (size.QuadPart > 0) {
            DWORD read = 0;
            ok = ReadFile(file, out->data(), static_cast<DWORD>(out->size()), &read, nullptr) != 0 &&
                 read == out->size();
        }
        CloseHandle(file);
        return ok;
    }
}

bool IniFile::Load(const std::wstring& path) {
    sections_.clear();
    sections_.push_back(Section{}); // the implicit, unnamed leading section

    std::string text;
    bool missing = false;
    if (!ReadWholeFile(path, &text, &missing)) {
        return missing; // no file yet is the normal first-run case, not a failure
    }

    for (const std::string& line : SplitLines(text)) {
        std::string trimmed = Trim(line);

        if (trimmed.size() >= 2 && trimmed.front() == '[' && trimmed.back() == ']') {
            Section section;
            section.name = Trim(trimmed.substr(1, trimmed.size() - 2));
            sections_.push_back(std::move(section));
            continue;
        }

        // Comments and blanks are kept verbatim; so is anything with no '=' at all, which is
        // malformed but still someone's content and cheaper to preserve than to judge.
        size_t equals = trimmed.empty() || trimmed.front() == ';' || trimmed.front() == '#'
                             ? std::string::npos
                             : trimmed.find('=');
        Line entry;
        if (equals == std::string::npos) {
            entry.raw = line;
        } else {
            entry.key = Trim(trimmed.substr(0, equals));
            entry.value = Trim(trimmed.substr(equals + 1));
            if (entry.key.empty()) {
                entry.value.clear();
                entry.raw = line; // "= value" with no key - preserve, don't index
            }
        }
        sections_.back().lines.push_back(std::move(entry));
    }

    return true;
}

bool IniFile::Save(const std::wstring& path) const {
    std::string text;
    for (const Section& section : sections_) {
        if (!section.name.empty()) {
            text += "[" + section.name + "]\r\n";
        }
        for (const Line& line : section.lines) {
            text += line.key.empty() ? line.raw : line.key + " = " + line.value;
            text += "\r\n";
        }
    }

    HANDLE file = CreateFileW(path.c_str(), GENERIC_WRITE, FILE_SHARE_READ, nullptr, CREATE_ALWAYS,
                               FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) {
        return false;
    }

    DWORD written = 0;
    bool ok = text.empty() ||
              (WriteFile(file, text.data(), static_cast<DWORD>(text.size()), &written, nullptr) !=
                    0 &&
               written == text.size());
    CloseHandle(file);
    return ok;
}

bool IniFile::IsEmpty() const {
    for (const Section& section : sections_) {
        if (!section.name.empty() || !section.lines.empty()) {
            return false;
        }
    }
    return true;
}

void IniFile::AddPreambleComment(const std::string& text) {
    Line line;
    line.raw = text.empty() ? std::string() : "; " + text;
    sections_.front().lines.push_back(std::move(line));
}

const IniFile::Section* IniFile::FindSection(const std::string& name) const {
    for (const Section& section : sections_) {
        if (section.name == name) {
            return &section;
        }
    }
    return nullptr;
}

IniFile::Section& IniFile::EnsureSection(const std::string& name) {
    for (Section& section : sections_) {
        if (section.name == name) {
            return section;
        }
    }

    // Separate groups with a blank line, added to the document once here at creation and then
    // preserved like any other line. Synthesizing it in Save() instead would stack a fresh blank
    // on top of the one already parsed back in, growing the file by a line per group per write.
    if (!IsEmpty()) {
        Section& previous = sections_.back();
        bool alreadyBlank = !previous.lines.empty() && previous.lines.back().key.empty() &&
                             previous.lines.back().raw.empty();
        if (!alreadyBlank) {
            previous.lines.push_back(Line{});
        }
    }

    Section section;
    section.name = name;
    sections_.push_back(std::move(section));
    return sections_.back();
}

const std::string* IniFile::Find(const std::string& section, const std::string& key) const {
    const Section* found = FindSection(section);
    if (found == nullptr) {
        return nullptr;
    }
    for (const Line& line : found->lines) {
        if (line.key == key) {
            return &line.value;
        }
    }
    return nullptr;
}

void IniFile::Set(const std::string& section, const std::string& key, const std::string& value) {
    Section& target = EnsureSection(section);
    for (Line& line : target.lines) {
        if (line.key == key) {
            line.value = value;
            return;
        }
    }

    Line line;
    line.key = key;
    line.value = value;

    // After the group's last real line, not after the blank separator trailing it - appending past
    // the separator would strand it mid-group and leave the next group with no spacing.
    size_t insertAt = target.lines.size();
    while (insertAt > 0) {
        const Line& previous = target.lines[insertAt - 1];
        if (!previous.key.empty() || !previous.raw.empty()) {
            break;
        }
        --insertAt;
    }
    target.lines.insert(target.lines.begin() + static_cast<ptrdiff_t>(insertAt), std::move(line));
}

} // namespace FCSE
