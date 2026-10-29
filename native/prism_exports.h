#pragma once
#ifdef _WIN32
#define SIM_API extern "C" __declspec(dllexport)
#else
#define SIM_API extern "C" __attribute__((visibility("default")))
#endif
