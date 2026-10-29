# Third-party components

Prism uses .NET, Avalonia, CommunityToolkit.Mvvm, Silk.NET, Open CASCADE Technology, Eigen, Netgen, and their dependencies. Runtime packages retain their bundled notices where present. Native libraries are dynamically linked.

Open CASCADE Technology: https://dev.opencascade.org/ · https://github.com/Open-Cascade-SAS/OCCT

Eigen: https://eigen.tuxfamily.org/

Netgen: https://ngsolve.org/ · https://github.com/NGSolve/netgen

.NET: https://github.com/dotnet/runtime

Avalonia: https://github.com/AvaloniaUI/Avalonia

The native build helper copies vcpkg dependency copyright/license files into the staged `licenses` directory. Retain these when distributing packages. Windows packages also include the required Microsoft Visual C++ runtime libraries; their distribution terms are supplied with the Microsoft build tools/runtime distribution.

CalculiX and ffmpeg are optional external tools and are not bundled in the default installer.
