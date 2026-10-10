# shim/CMakeLists.txt - builds the ONE native library `codebrix_vorbis` for CodeBrix.Audio.Vorbis.
#
# libogg 1.3.6 + libvorbis 1.3.7 (+ libvorbisenc) are compiled from the UNMODIFIED Xiph trees in
# ../ogg and ../vorbis as static object libraries and linked into one shared library together with
# codebrix_vorbis.c. Only the functions declared in codebrix_vorbis.h are exported.
#
# Why the upstream sources are listed here instead of add_subdirectory(): libvorbis's CMakeLists
# requires find_package(Ogg) (an INSTALLED libogg), always builds libvorbisfile, and on WIN32 folds
# vorbisenc + .def export files into the library; libogg's adds install/pkg-config/doc rules. None of
# that is wanted here, so the shim names the exact upstream sources instead (same file lists as
# ogg/CMakeLists.txt and vorbis/lib/CMakeLists.txt, minus vorbisfile.c). Nothing upstream is edited;
# ogg's config_types.h is generated into the build tree from ogg/include/ogg/config_types.h.in.
#
# Options:
#   CBV_BUILD_SMOKE_TEST=ON   also build ../smoke-test.c as `codebrix_vorbis_smoke` (host builds only)
cmake_minimum_required(VERSION 3.16)

project(codebrix_vorbis LANGUAGES C)

option(CBV_BUILD_SMOKE_TEST "Build the smoke-test executable" OFF)

set(CBV_TOOLS_DIR  "${CMAKE_CURRENT_SOURCE_DIR}/..")
set(CBV_OGG_DIR    "${CBV_TOOLS_DIR}/ogg")
set(CBV_VORBIS_DIR "${CBV_TOOLS_DIR}/vorbis")

if(NOT EXISTS "${CBV_OGG_DIR}/src/framing.c" OR NOT EXISTS "${CBV_VORBIS_DIR}/lib/vorbisenc.c")
    message(FATAL_ERROR "Upstream Xiph trees not found under ${CBV_TOOLS_DIR} (ogg/, vorbis/).")
endif()

if(NOT CMAKE_BUILD_TYPE AND NOT CMAKE_CONFIGURATION_TYPES)
    set(CMAKE_BUILD_TYPE Release)
endif()

# Every object in the shared library is compiled with hidden visibility; codebrix_vorbis.h marks
# its own functions CBV_EXPORT (visibility("default") / __declspec(dllexport)).
set(CMAKE_C_VISIBILITY_PRESET hidden)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)

include(CheckIncludeFiles)
include(CheckLibraryExists)

# ---- ogg/config_types.h (generated, exactly as ogg/CMakeLists.txt does) -------------------------
check_include_files(inttypes.h INCLUDE_INTTYPES_H)
check_include_files(stdint.h INCLUDE_STDINT_H)
check_include_files(sys/types.h INCLUDE_SYS_TYPES_H)
foreach(v INCLUDE_INTTYPES_H INCLUDE_STDINT_H INCLUDE_SYS_TYPES_H)
    if(${v})
        set(${v} 1)
    else()
        set(${v} 0)
    endif()
endforeach()
set(SIZE16 int16_t)
set(USIZE16 uint16_t)
set(SIZE32 int32_t)
set(USIZE32 uint32_t)
set(SIZE64 int64_t)
set(USIZE64 uint64_t)
configure_file("${CBV_OGG_DIR}/include/ogg/config_types.h.in"
               "${CMAKE_CURRENT_BINARY_DIR}/generated/ogg/config_types.h" @ONLY)

if(NOT WIN32)
    check_include_files(alloca.h HAVE_ALLOCA_H)
    check_library_exists(m floor "" HAVE_LIBM)
endif()

if(MSVC)
    set(CBV_MSVC_DEFS _CRT_SECURE_NO_WARNINGS _CRT_SECURE_NO_DEPRECATE _CRT_NONSTDC_NO_DEPRECATE)
endif()

# ---- libogg (static objects) --------------------------------------------------------------------
add_library(cbv_ogg OBJECT
    "${CBV_OGG_DIR}/src/bitwise.c"
    "${CBV_OGG_DIR}/src/framing.c")
target_include_directories(cbv_ogg PUBLIC
    "${CBV_OGG_DIR}/include"
    "${CMAKE_CURRENT_BINARY_DIR}/generated")
target_compile_definitions(cbv_ogg PRIVATE ${CBV_MSVC_DEFS})

# ---- libvorbis + libvorbisenc (static objects; NO vorbisfile) -----------------------------------
set(CBV_VORBIS_SOURCES
    mdct.c smallft.c block.c envelope.c window.c lsp.c lpc.c analysis.c synthesis.c psy.c info.c
    floor1.c floor0.c res0.c mapping0.c registry.c codebook.c sharedbook.c lookup.c bitrate.c
    vorbisenc.c)
list(TRANSFORM CBV_VORBIS_SOURCES PREPEND "${CBV_VORBIS_DIR}/lib/")
add_library(cbv_vorbis OBJECT ${CBV_VORBIS_SOURCES})
target_include_directories(cbv_vorbis
    PUBLIC  "${CBV_VORBIS_DIR}/include"
    PRIVATE "${CBV_VORBIS_DIR}/lib")
target_link_libraries(cbv_vorbis PUBLIC cbv_ogg)
target_compile_definitions(cbv_vorbis PRIVATE ${CBV_MSVC_DEFS} $<$<BOOL:${HAVE_ALLOCA_H}>:HAVE_ALLOCA_H>)
# Upstream code; its warnings are not ours to fix (the shim itself builds with -Wall -Wextra / /W4).
if(MSVC)
    target_compile_options(cbv_vorbis PRIVATE /w)
    target_compile_options(cbv_ogg PRIVATE /w)
else()
    target_compile_options(cbv_vorbis PRIVATE -w)
    target_compile_options(cbv_ogg PRIVATE -w)
endif()

# ---- the shared library -------------------------------------------------------------------------
add_library(codebrix_vorbis SHARED
    codebrix_vorbis.c
    $<TARGET_OBJECTS:cbv_ogg>
    $<TARGET_OBJECTS:cbv_vorbis>)
target_include_directories(codebrix_vorbis PUBLIC "${CMAKE_CURRENT_SOURCE_DIR}")
target_link_libraries(codebrix_vorbis PRIVATE cbv_vorbis)
target_compile_definitions(codebrix_vorbis PRIVATE ${CBV_MSVC_DEFS})
if(MSVC)
    target_compile_options(codebrix_vorbis PRIVATE /W4)
else()
    target_compile_options(codebrix_vorbis PRIVATE -Wall -Wextra)
endif()
if(HAVE_LIBM)
    target_link_libraries(codebrix_vorbis PRIVATE m)
endif()
# No SONAME version suffix: the managed loader probes the plain file name on every RID.
if(WIN32)
    set_target_properties(codebrix_vorbis PROPERTIES PREFIX "")
endif()
if(CMAKE_SYSTEM_NAME STREQUAL "Linux" OR CMAKE_SYSTEM_NAME STREQUAL "Android")
    target_link_options(codebrix_vorbis PRIVATE
        "LINKER:--no-undefined"
        "LINKER:--exclude-libs,ALL"
        "LINKER:-z,noexecstack"
        "LINKER:--build-id=sha1")
endif()
if(APPLE)
    set_target_properties(codebrix_vorbis PROPERTIES INSTALL_NAME_DIR "@rpath")
endif()

# ---- smoke test ---------------------------------------------------------------------------------
if(CBV_BUILD_SMOKE_TEST)
    add_executable(codebrix_vorbis_smoke "${CBV_TOOLS_DIR}/smoke-test.c")
    target_link_libraries(codebrix_vorbis_smoke PRIVATE codebrix_vorbis)
    target_include_directories(codebrix_vorbis_smoke PRIVATE "${CMAKE_CURRENT_SOURCE_DIR}")
endif()
