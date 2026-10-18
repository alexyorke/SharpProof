#!/usr/bin/env bash
exec "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/build.sh" --ci --restore --build --test --integrationTest "$@"
