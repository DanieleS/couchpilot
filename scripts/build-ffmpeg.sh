#!/usr/bin/env bash
# A small ffmpeg.exe for the gallery's video thumbnails and the clip editor, cross-built for
# 64-bit Windows from Linux (CI runs it on ubuntu-latest). Just what those two need:
#
#   in   the MP4s Game Bar and NVIDIA write: H.264, HEVC (8 and 10 bit), AV1 (dav1d), AAC sound
#   out  one JPEG on stdout (a thumbnail, or a strip of frames for the trimmer);
#        a piece of a clip as MP4 (H.264 with x264, AAC) or as GIF (with its own palette)
#   on   the way, scaled down and, for HDR clips, tone-mapped to SDR (zimg)
#
# Everything static, no DLLs: a single file next to CouchPilot.dll.
#
#   scripts/build-ffmpeg.sh [work dir] [out dir]
#
# Sources and intermediate files go in the work dir (default build/ffmpeg), ffmpeg.exe and its
# readme in the out dir (default src/CouchPilot/ffmpeg, where the .csproj picks them up).
#
# Needs: git, mingw-w64, nasm, meson, ninja, autoconf, automake, libtool, pkg-config.
set -euo pipefail

FFMPEG_VERSION=n7.1.1
DAV1D_VERSION=1.5.1
ZIMG_VERSION=release-3.0.5
X264_VERSION=stable
HOST=x86_64-w64-mingw32

WORK=$(realpath -m "${1:-build/ffmpeg}")
OUT=$(realpath -m "${2:-src/CouchPilot/ffmpeg}")
PREFIX=$WORK/prefix
JOBS=$(nproc)
mkdir -p "$WORK" "$PREFIX" "$OUT"
cd "$WORK"

fetch() { # name url tag
  [ -d "$1" ] || git clone -q --depth 1 -b "$3" "$2" "$1"
}
fetch dav1d https://github.com/videolan/dav1d "$DAV1D_VERSION"
fetch zimg https://github.com/sekrit-twc/zimg "$ZIMG_VERSION"
# From GitHub's mirror of code.videolan.org, like dav1d: one host for every source.
fetch x264 https://github.com/mirror/x264 "$X264_VERSION"
fetch ffmpeg https://github.com/FFmpeg/FFmpeg "$FFMPEG_VERSION"

export PKG_CONFIG_PATH=$PREFIX/lib/pkgconfig
export PKG_CONFIG_LIBDIR=$PREFIX/lib/pkgconfig

# AV1: dav1d, the decoder browsers use.
if [ ! -f "$PREFIX/lib/libdav1d.a" ]; then
  cat > cross.txt <<EOF
[binaries]
c = '$HOST-gcc'
cpp = '$HOST-g++'
ar = '$HOST-ar'
strip = '$HOST-strip'
windres = '$HOST-windres'
pkg-config = 'pkg-config'

[host_machine]
system = 'windows'
cpu_family = 'x86_64'
cpu = 'x86_64'
endian = 'little'
EOF
  meson setup dav1d/build dav1d --cross-file cross.txt --prefix "$PREFIX" --libdir lib \
    --default-library=static --buildtype=release -Denable_tools=false -Denable_tests=false
  ninja -C dav1d/build install
fi

# HDR → SDR: zimg, for the zscale filter.
if [ ! -f "$PREFIX/lib/libzimg.a" ]; then
  (cd zimg && ./autogen.sh && ./configure --host=$HOST --prefix="$PREFIX" --enable-static --disable-shared \
    && make -j"$JOBS" && make install)
fi

# H.264 for the clips the editor writes: x264, the encoder every phone plays back.
if [ ! -f "$PREFIX/lib/libx264.a" ]; then
  (cd x264 && ./configure --host=$HOST --cross-prefix=$HOST- --prefix="$PREFIX" --enable-static --disable-cli \
      --disable-opencl --enable-pic --disable-lavf --disable-swscale --disable-avs --disable-ffms --disable-gpac --disable-lsmash \
    && make -j"$JOBS" && make install)
fi

cd ffmpeg
./configure \
  --prefix="$PREFIX" --arch=x86_64 --target-os=mingw32 --cross-prefix=$HOST- \
  --pkg-config=pkg-config --pkg-config-flags=--static \
  --extra-ldflags=-static --extra-libs=-lstdc++ \
  --enable-gpl --enable-version3 --enable-small \
  --disable-everything --disable-autodetect --disable-network --disable-doc --disable-debug \
  --disable-ffplay --disable-ffprobe --disable-avdevice --disable-postproc \
  --enable-w32threads \
  --enable-libdav1d --enable-libzimg --enable-libx264 \
  --enable-protocol=file,pipe \
  --enable-demuxer=mov \
  --enable-parser=h264,hevc,av1,aac \
  --enable-decoder=h264,hevc,libdav1d,aac \
  --enable-filter=scale,zscale,tonemap,format,null,fps,tile,split,palettegen,paletteuse,aresample,aformat,anull,trim,atrim,setpts,asetpts \
  --enable-encoder=mjpeg,libx264,aac,gif \
  --enable-muxer=image2pipe,mp4,gif
make -j"$JOBS"
$HOST-strip -o "$OUT/ffmpeg.exe" ffmpeg.exe

# GPL: say what this is and where its source lives.
cat > "$OUT/ffmpeg-readme.txt" <<EOF
ffmpeg.exe makes the thumbnails of the clips in CouchPilot's gallery, and cuts the pieces
of them the phone asks for, as MP4 or GIF.

FFmpeg $FFMPEG_VERSION (https://github.com/FFmpeg/FFmpeg/tree/$FFMPEG_VERSION), GPL v3,
with dav1d $DAV1D_VERSION (https://github.com/videolan/dav1d, BSD-2-Clause),
zimg $ZIMG_VERSION (https://github.com/sekrit-twc/zimg, WTFPL) and
x264 ($X264_VERSION branch, https://code.videolan.org/videolan/x264, GPL v2 or later).
Built by scripts/build-ffmpeg.sh in the CouchPilot repository, which lists every option.
EOF
ls -la "$OUT/ffmpeg.exe"
