cmake_minimum_required(VERSION 3.20)
project(chessai CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
if(NOT CMAKE_BUILD_TYPE)
  set(CMAKE_BUILD_TYPE Release)
endif()

include(CheckCXXCompilerFlag)
check_cxx_compiler_flag("-mcpu=apple-m4" HAS_APPLE_M4)
if(HAS_APPLE_M4)
  set(CPU_FLAG "-mcpu=apple-m4")
else()
  set(CPU_FLAG "-mcpu=native")
endif()

set(CMAKE_CXX_FLAGS_RELEASE "-O3 ${CPU_FLAG} -flto -DNDEBUG")
set(CMAKE_CXX_FLAGS_DEBUG "-O1 -g -fsanitize=address,undefined")
set(CMAKE_EXE_LINKER_FLAGS_DEBUG "-fsanitize=address,undefined")

add_executable(chessai
  src/main.cpp
  src/bitboard.cpp
  src/position.cpp
  src/movegen.cpp
  src/eval.cpp
  src/search.cpp
  src/uci.cpp)
target_compile_options(chessai PRIVATE -Wall -Wextra)
