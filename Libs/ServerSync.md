# Pinned ServerSync input

- Baseline: `valheim-1.0.7-r1`, assembly version `1.0.0.0`, file version `1.0.0.1`.
- SHA-256: `b4dd786997f4e90d770f09ef3e9d64154754fe7e8edfb4841795751895b35846`.
- Source: https://github.com/blaxxun-boop/ServerSync/tree/c57c2aa54e07cdcc7630d6068699ea781622323e (MIT-0; license retained alongside the DLL).
- Reviewed source build uses original Valheim 1.0.7 references, public admin checks, and preserves login list/time packet ordering while configuration is synchronized.
- Replaces the unchanged baseline DLL `166956302a294e224474b26f4c7d58409084ad3f48bd0af1feb7551f229c8f60`.
- Vendored here and internalized into HarnessPrefabs by the existing ILRepack target. Do not install this library as a standalone BepInEx plugin.
- Reproduction sources and verification: `C:/Users/blizz/.codex/references/valheim/integrations/serversync/versions/valheim-1.0.7-r1/`.
- Library static/isolated validation is distinct from actual host, dedicated server and crossplay testing of this mod.
