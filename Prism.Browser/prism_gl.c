// Exposes Emscripten's WebGL entry points to .NET: Silk.NET loads every GL function through prism_gl_proc.
#include <emscripten.h>
#include <emscripten/html5.h>

extern void* emscripten_GetProcAddress(const char* name);

void* prism_gl_proc(const char* name) { return emscripten_GetProcAddress(name); }
int prism_gl_context(void) { return (int)emscripten_webgl_get_current_context(); }

// Signature shapes only (see GlSignatures.cs); never called.
int prism_sig_i_(void) { return 0; }
int prism_sig_i_i(int a0) { return 0; }
int prism_sig_i_ii(int a0, int a1) { return 0; }
int prism_sig_i_iii(int a0, int a1, int a2) { return 0; }
void prism_sig_v_ff(float a0, float a1) { }
void prism_sig_v_ffff(float a0, float a1, float a2, float a3) { }
void prism_sig_v_i(int a0) { }
void prism_sig_v_idd(int a0, double a1, double a2) { }
void prism_sig_v_idddd(int a0, double a1, double a2, double a3, double a4) { }
void prism_sig_v_iff(int a0, float a1, float a2) { }
void prism_sig_v_iffff(int a0, float a1, float a2, float a3, float a4) { }
void prism_sig_v_ii(int a0, int a1) { }
void prism_sig_v_iif(int a0, int a1, float a2) { }
void prism_sig_v_iii(int a0, int a1, int a2) { }
void prism_sig_v_iiii(int a0, int a1, int a2, int a3) { }
void prism_sig_v_iiiii(int a0, int a1, int a2, int a3, int a4) { }
void prism_sig_v_iiiiii(int a0, int a1, int a2, int a3, int a4, int a5) { }
void prism_sig_v_iiiiiiiii(int a0, int a1, int a2, int a3, int a4, int a5, int a6, int a7, int a8) { }
void prism_sig_v_iiiiiiiiii(int a0, int a1, int a2, int a3, int a4, int a5, int a6, int a7, int a8, int a9) { }
