# Test-only Python runtime; the shipping toolchain and consumer image stay separate.
FROM python:3.13-slim-bookworm@sha256:a5a7017efc5969e00ae2b91c8eb0631049a569652aa4ffc4501769049ae45c72 AS python
FROM canonical_tooling AS consumer-baseline-tests
USER root
COPY --from=python /usr/local/ /opt/sharpproof/python/
ENV PYTHONHOME=/opt/sharpproof/python \
    PYTHONDONTWRITEBYTECODE=1 \
    PYTHONUNBUFFERED=1 \
    PATH="/opt/sharpproof/python/bin:${PATH}"
# Fail during the build if the reviewed runtime or its required native modules
# cannot run on the canonical base. The test container never installs packages.
RUN test "${SHARPPROOF_CONTAINER}" = "1" \
    && test -f "${SHARPPROOF_CONTAINER_CONTRACT}" \
    && test "$(uname -m)" = "x86_64" \
    && python3 -c 'import bz2, hashlib, json, lzma, pathlib, ssl, sys, tempfile, unittest, urllib.request, zipfile, zlib; assert sys.version_info[:3] == (3, 13, 17), sys.version; print(sys.version)'
USER sharpproof
WORKDIR /workspace/SharpProof
ENTRYPOINT ["/opt/sharpproof/python/bin/python3", "-B"]
CMD ["eng/scripts/Test-SharpProofConsumerBaseline.py"]
