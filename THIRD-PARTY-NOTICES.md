# Third-party notices

APS Generator distributions include third-party components under their own
licenses. The applicable license texts are provided in the `LICENSES`
directory beside this file.

## CryptoMiniSat 5.16.0

CryptoMiniSat provides the constraint solver used by both the standalone
application and the mod. Its runtime/build/link components are MIT licensed.
See `LICENSES/CryptoMiniSat-LICENSE.txt`.

Source: https://github.com/msoos/cryptominisat/tree/2bf89cfb3e126e559d472388486327813f735f7e

The release build pins CaDiCaL (`394c3f72858c2fe8cd35321f74f11f0f61c91123`)
and CaDiBaCk (`3b6a84062b1304433eb8960a4bff6b9a80de9c54`), embedded in the
solver under their MIT licenses. Their license texts ship alongside CryptoMiniSat.
Build preparation adapts only dependency-version reporting for these revisions.

## GNU MP

The Linux distributions include the GNU MP shared libraries `libgmp.so.10`
and `libgmpxx.so.4`, used by CryptoMiniSat. Windows distributions also include
GNU MP when it is a runtime dependency of the Windows CryptoMiniSat build. GNU
MP is distributed under the GNU Lesser General Public License version 3 or
later, or alternatively the GNU General Public License version 2 or later. APS
Generator uses the LGPLv3+ option. You may replace dynamically linked copies
with interface-compatible versions.

See `LICENSES/GMP-LGPL-3.0-or-later.txt` for the license and
`LICENSES/GMP-SOURCE.txt` for corresponding-source information.

Source: https://gmplib.org/

## Harmony 2.2.2

The mod distribution includes the merged `0Harmony.dll` runtime patching
library under the MIT License. See `LICENSES/Harmony-MIT.txt`.

Source: https://github.com/pardeike/Harmony/tree/v2.2.2.0

The merged Harmony assembly also incorporates these MIT-licensed components:

- Mono.Cecil 0.10.4 — see `LICENSES/Mono.Cecil-MIT.txt`.
- MonoMod.Common 22.6.3.1 — see `LICENSES/MonoMod-MIT.txt`.

Harmony and its merged components are included by the mod distribution only;
the standalone application does not use them.
