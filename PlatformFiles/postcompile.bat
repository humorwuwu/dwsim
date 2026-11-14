del ..\DistPackages\Windows\DWSIM.vshost.*
del ..\DistPackages\Windows\*.tmp
del ..\DistPackages\Windows\*.dylib
del ..\DistPackages\Windows\*.so

del ..\DistPackages\macOS\DWSIM.vshost.*
del ..\DistPackages\macOS\*.tmp



del ..\DistPackages\Windows\ExcelDna.Integration.dll
del ..\DistPackages\macOS\ExcelDna.Integration.dll

del ..\DistPackages\Windows\plugins\*Skia*
del ..\DistPackages\Windows\plugins\*Eto*
rmdir /s /q ..\DistPackages\Windows\plugins\x86
rmdir /s /q ..\DistPackages\Windows\plugins\x64
rmdir /s /q ..\DistPackages\Windows\x86
rmdir /s /q ..\DistPackages\Windows\x64


del ..\DistPackages\macOS\plugins\*Skia*
del ..\DistPackages\macOS\plugins\*Eto*
rmdir /s /q ..\DistPackages\macOS\plugins\x86
rmdir /s /q ..\DistPackages\macOS\plugins\x64


xcopy "..\DWSIM\Lib\*" "..\DistPackages\Windows\Lib\*" /E /Y /F /D
xcopy "..\DWSIM\Lib\*" "..\DistPackages\macOS\Lib\*" /E /Y /F /D

xcopy "..\DistPackages\Windows\*" "..\DistPackages\Windows_Plus\*" /E /Y /F /D

rmdir /s /q ..\DistPackages\Windows_Plus\x86
rmdir /s /q ..\DistPackages\Windows_Plus\x64


rmdir /s /q ..\DistPackages\macOS\samples
rmdir /s /q ..\DistPackages\Windows\samples
rmdir /s /q ..\DistPackages\Windows_Plus\samples

xcopy "Common\samples\*" "..\DistPackages\macOS\samples\*" /E /Y /F /D
xcopy "Common\samples\*" "..\DistPackages\Windows\samples\*" /E /Y /F /D
xcopy "Common\samples\*" "..\DistPackages\Windows_Plus\samples\*" /E /Y /F /D

del ..\DistPackages\Windows\plugins\FileHelpers.dll
del ..\DistPackages\Windows_Plus\plugins\FileHelpers.dll
del ..\DistPackages\macOS\plugins\FileHelpers.dll

del ..\DistPackages\Windows\plugins\netstandard.dll
del ..\DistPackages\Windows_Plus\plugins\netstandard.dll
del ..\DistPackages\macOS\plugins\netstandard.dll

del ..\DistPackages\Windows\plugins\System*.dll
del ..\DistPackages\Windows_Plus\plugins\System*.dll
del ..\DistPackages\macOS\plugins\System*.dll

del ..\DistPackages\Windows\plugins\Microsoft*.dll
del ..\DistPackages\Windows_Plus\plugins\Microsoft*.dll
del ..\DistPackages\macOS\plugins\Microsoft*.dll

del ..\DistPackages\Windows\plugins\*.xml
del ..\DistPackages\Windows_Plus\plugins\*.xml
del ..\DistPackages\macOS\plugins\*.xml