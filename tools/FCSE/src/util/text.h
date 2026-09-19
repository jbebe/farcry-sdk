#pragma once

#include <string>
#include <vector>

namespace FCSE {

// Splits on '\n' and drops a trailing '\r', so CRLF and LF text read identically. The final line
// is returned whether or not the text ends with a newline.
std::vector<std::string> SplitLines(const std::string& text);

}
