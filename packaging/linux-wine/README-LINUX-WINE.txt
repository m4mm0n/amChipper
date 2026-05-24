amChipper Linux WPF compatibility package
=========================================

This package runs the Windows WPF build through Wine. It is not a native Linux
WPF runtime because Microsoft WPF remains Windows-only under .NET.

Requirements
------------

1. Wine 9 or newer is recommended.
2. Install the Windows x64 Microsoft .NET Desktop Runtime 10.x inside the same
   Wine prefix.

Quick start
-----------

chmod +x run-amChipper.sh
./run-amChipper.sh

If the launcher reports that Microsoft.WindowsDesktop.App 10.x is missing,
download the Windows x64 Desktop Runtime from:

https://dotnet.microsoft.com/download/dotnet/10.0

Then install it into Wine, for example:

wine windowsdesktop-runtime-10.*-win-x64.exe

Notes
-----

- The real application files live in ./amChipper.
- Logs still write to the app-configured amChipper log directory inside the
  Wine environment.
- A native Linux UI would require a port to a cross-platform XAML stack such as
  Avalonia. This package keeps the current WPF UI usable on Linux first.
