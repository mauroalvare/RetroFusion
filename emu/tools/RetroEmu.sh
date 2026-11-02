#!/bin/sh
# Independent preflight: works before the managed runtime, never installs or downloads.
root=$(CDPATH= cd -P "$(dirname "$0")" && pwd) || exit 2
language=es
previous=
check=no
for argument do
    if [ "$previous" = --language ]; then
        case "$argument" in en|es) language=$argument ;; esac
    fi
    case "$argument" in --check|--comprobar) check=yes ;; esac
    previous=$argument
done
say() { if [ "$language" = en ]; then printf '%s\n' "$2"; else printf '%s\n' "$1"; fi; }
failed=no
if [ "$(uname -s)" != Linux ] || [ "$(uname -m)" != x86_64 ]; then
    say 'Este paquete necesita Linux x64 (x86_64) con glibc. Elegí el paquete para tu sistema.' 'This package needs Linux x64 (x86_64) with glibc. Choose the package for your system.'
    exit 2
fi
libc=$(LC_ALL=C getconf GNU_LIBC_VERSION 2>/dev/null) || libc=
version=${libc#glibc }
major=${version%%.*}; minor=${version#*.}
compatible=no
case "$libc" in
    'glibc '*)
        case "$major.$minor" in
            *[!0-9.]*|.*|*.) ;;
            *) if [ "$major" -gt 2 ] 2>/dev/null || { [ "$major" -eq 2 ] && [ "$minor" -ge 38 ]; } 2>/dev/null; then compatible=yes; fi ;;
        esac ;;
esac
if [ "$compatible" != yes ]; then
    say 'Las bibliotecas gráficas de este paquete necesitan glibc 2.38 o posterior. No reemplaces glibc por separado: revisá una actualización oficial de tu distribución.' 'The graphics libraries in this package need glibc 2.38 or later. Do not replace glibc separately: check an official distribution upgrade.'
    printf 'glibc: %s\n' "${libc:-?}"
    failed=yes
fi
for file in RetroEmu libhostfxr.so libhostpolicy.so libcoreclr.so RetroEmu.dll Retro.Core.dll libraylib.so libcimgui.so; do
    if [ ! -s "$root/$file" ]; then
        say 'Falta un archivo del paquete; extraé la copia completa o repará el programa:' 'A package file is missing; extract the complete copy or repair the program:'
        printf '%s\n' "$file"; failed=yes
    fi
done
if [ -f "$root/RetroEmu" ] && [ ! -x "$root/RetroEmu" ]; then
    say 'RetroEmu no tiene permiso de ejecución. Conservá los permisos al extraer el tar.gz o habilitá Ejecutar como programa en sus propiedades.' 'RetroEmu lacks execute permission. Preserve permissions when extracting the tar.gz or enable Run as a program in its properties.'
    failed=yes
fi
if [ "$failed" = no ]; then
    if command -v ldd >/dev/null 2>&1; then
        dependencies=$(LC_ALL=C ldd "$root/libraylib.so" "$root/libcimgui.so" "$root/RetroEmu" 2>&1)
        dependency_status=$?
        case "$dependencies" in
            *'not found'*)
                say 'Faltan bibliotecas del sistema. Usá los paquetes oficiales de tu distribución; no un instalador de DLL ajeno.' 'System libraries are missing. Use official distribution packages, not a third-party DLL installer.'
                printf '%s\n' "$dependencies"; failed=yes ;;
        esac
        if [ "$dependency_status" -ne 0 ] && [ "$failed" = no ]; then
            say 'No se pudieron comprobar las bibliotecas. Revisá el paquete completo y el diagnóstico del sistema:' 'The libraries could not be checked. Check the complete package and the system diagnostic:'
            printf '%s\n' "$dependencies"; failed=yes
        fi
    else
        say 'No se encontró ldd; las dependencias nativas siguen sin comprobar.' 'ldd was not found; native dependencies remain unchecked.'
        failed=yes
    fi
fi
if [ -z "${DISPLAY:-}" ]; then
    say 'No hay una sesión X11 disponible (DISPLAY). En Wayland necesitás XWayland para estas bibliotecas.' 'No X11 session is available (DISPLAY). These libraries need XWayland when using Wayland.'
    failed=yes
fi
if [ "$failed" = yes ]; then
    say 'Guía oficial de requisitos; este comprobador no descarga ni instala componentes:' 'Official requirements guide; this checker does not download or install components:'
    printf '%s\n' 'https://learn.microsoft.com/en-us/dotnet/core/install/linux'
    exit 2
fi
if [ "$check" = yes ]; then
    say 'Archivos y dependencias enlazadas presentes. No se comprobaron OpenGL, audio ni las bibliotecas cargadas dinámicamente (ICU/OpenSSL). No se inició el programa.' 'Package files and linked dependencies are present. OpenGL, audio and dynamically loaded libraries (ICU/OpenSSL) were not checked. The program was not started.'
    exit 0
fi
cd "$root" || exit 2
exec "$root/RetroEmu" "$@"
