"""Run the production DLL and checks in an isolated headless copy of the game.

No test assembly is installed into the live game. The test bootstrap exits inside
Awake, before loading a player save. Set PARTY_QA_KEEP=1 to retain the test player.
"""
import os
import json
import shutil
import subprocess
import sys
import tempfile
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
PROJECT = Path(__file__).resolve().parents[1]


def copy_tree(source, destination):
    for entry in source.rglob("*"):
        target = destination / entry.relative_to(source)
        if entry.is_file():
            target.parent.mkdir(parents=True, exist_ok=True)
            try:
                os.link(entry, target)
            except OSError:
                shutil.copy2(entry, target)


def run():
    subprocess.run(["dotnet", "build", str(PROJECT / "Tests" / "Checks.csproj"),
                    "-c", "Release", "--nologo"], check=True)
    parent = ROOT / "artifacts"
    directory = Path(tempfile.mkdtemp(prefix="SilverpinePartyQA-", dir=parent))
    try:
        for name in ("Silverpine.exe", "UnityPlayer.dll", "winhttp.dll", "doorstop_config.ini"):
            shutil.copy2(ROOT / name, directory / name)
        for name in ("MonoBleedingEdge", "BepInEx/core", "Silverpine_Data/Managed",
                     "Silverpine_Data/Plugins", "Silverpine_Data/Resources"):
            copy_tree(ROOT / name, directory / name)
        for path in (ROOT / "Silverpine_Data").iterdir():
            if path.is_file():
                try:
                    os.link(path, directory / "Silverpine_Data" / path.name)
                except OSError:
                    shutil.copy2(path, directory / "Silverpine_Data" / path.name)
        (directory / "party-test-only").touch()
        plugins = directory / "BepInEx/plugins"
        definitions = directory / "BepInEx/config/CustomItemLoader/PartyChecks"
        definitions.mkdir(parents=True)
        coat = {"id": "coat", "name": "Party Check Coat", "clone": "Leather Chest Armor",
                "useCloneVisuals": True, "value": 15, "category": "Clothing",
                "attributeModifiers": {"maxHealth": 12, "fireArmor": 3, "speedBonus": 0.25}}
        cape = dict(coat, id="cape", name="Party Check Cape", equipmentSlot="silverpine.equipslots.back")
        (definitions / "items.json").write_text(json.dumps({"packId": "party.test", "items": [coat, cape]}))
        copy_tree(ROOT / "BepInEx/plugins/ModdingTools", plugins / "ModdingTools")
        shutil.copy2(PROJECT / "bin/Release/netstandard2.1/CompanionParty.dll", plugins)
        shutil.copy2(PROJECT / "Tests/bin/Release/netstandard2.1/CompanionParty.Checks.dll", plugins)
        optional = ("CustomItemLoader", "AdditionalEquipmentSlots", "AttributeExpansion")
        for label, additions in (("native", ()), ("rendered-native", ()), ("loader", optional[:1]),
                                 ("loader-slots", optional[1:2]), ("all", optional[2:]),
                                 ("loader-expansion", ()), ("native-restore", ()), ("restored", ()),
                                 ("dialogue-compatibility", ()), ("rendered-party", ())):
            if label == "dialogue-compatibility":
                # Exercise the installed gold bridge and dialogue parser together
                # with the same assembly preloaders used in the modded game.
                for name in ("SaltExtraDebug.dll", "SaltDialogueImpersonator.dll"):
                    shutil.copy2(ROOT / "BepInEx/plugins" / name, plugins)
                for patcher in (ROOT / "BepInEx/patchers").glob("*.dll"):
                    target = directory / "BepInEx/patchers" / patcher.name
                    target.parent.mkdir(exist_ok=True)
                    shutil.copy2(patcher, target)
            if label == "loader-expansion":
                (plugins / "AdditionalEquipmentSlots").rename(directory / "parked-slots")
            if label == "native-restore":
                for name in ("CustomItemLoader", "AttributeExpansion"):
                    (plugins / name).rename(directory / ("parked-" + name))
            if label == "restored":
                for name in ("CustomItemLoader", "AttributeExpansion"):
                    (directory / ("parked-" + name)).rename(plugins / name)
                (directory / "parked-slots").rename(plugins / "AdditionalEquipmentSlots")
            for name in additions:
                copy_tree(ROOT / "BepInEx/plugins" / name, plugins / name)
            if len(sys.argv) > 1 and label not in sys.argv[1:]:
                continue
            report = directory / "party-checks.log"
            report.unlink(missing_ok=True)
            cmd = [str(directory / "Silverpine.exe"), "-batchmode", "-nographics",
                   "-nolog", "-logFile", str(directory / "unity-checks.log")]
            if label in ("rendered-native", "rendered-party"):
                cmd.remove("-nographics")
                cmd.extend(["-party-render-check", "-screen-width", "1280", "-screen-height", "720"])
            print("Starting", label, flush=True)
            with subprocess.Popen(cmd, cwd=directory,
                                  creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0)) as game:
                deadline = time.monotonic() + 90
                while not report.exists() and game.poll() is None and time.monotonic() < deadline:
                    time.sleep(0.25)
                if game.poll() is None:
                    game.kill()
                game.wait()
            evidence = report.read_text(encoding="utf-8-sig") if report.exists() else ""
            print(label, evidence, flush=True)
            if not evidence.strip().endswith("PASS"):
                print("Test player:", directory, flush=True)
                for logfile in (directory / "BepInEx/LogOutput.log", directory / "unity-checks.log"):
                    if logfile.exists():
                        print(logfile.read_text(encoding="utf-8-sig", errors="replace")[-9000:])
                raise RuntimeError("Headless party checks failed in " + label)
    except BaseException:
        print("Retained failed test player:", directory, flush=True)
        raise
    else:
        if os.environ.get("PARTY_QA_KEEP") == "1":
            print("Test player:", directory)
        else:
            assert directory.resolve().parent == parent.resolve() and directory.name.startswith("SilverpinePartyQA-")
            shutil.rmtree(directory)


if __name__ == "__main__":
    run()
