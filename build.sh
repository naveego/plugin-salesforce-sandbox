#!/bin/bash

# Takes one parameter, the version to build

if [ -z $1 ]; then
  echo "must provide version"
  exit 1
fi

set -xe

version=$1

dotnet publish -c Release -r win-x64 --version-suffix $version
dotnet publish -c Release -r linux-x64 --version-suffix $version
dotnet publish -c Release -r osx-x64 --version-suffix $version