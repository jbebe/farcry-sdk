// The game's bin folder, beside fcse.ini, which is where a file of ours goes.
#pragma once

#include <filesystem>
#include <string>

namespace DevTools::BinFolder {

std::filesystem::path File(const wchar_t* name);

// The file names of the FCSE plugins loaded from bin\plugins\ and its subfolders, joined by commas.
const std::string& Plugins();

}
