// The game's bin folder, beside fcse.ini, which is where a file of ours goes.
#pragma once

#include <filesystem>

namespace DevTools::BinFolder {

std::filesystem::path File(const wchar_t* name);

}
