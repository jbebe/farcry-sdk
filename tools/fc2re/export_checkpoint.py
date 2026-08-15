# Export a program to a .gzf checkpoint (PyGhidra).
#
# The repo keeps these under reverse/ghidra/ to be re-imported into a fresh
# project. Writes a new file rather than replacing one, so an earlier
# known-good state stays recoverable.
#
#   python export_checkpoint.py C:\projdir fc2 /FarCry2_server out.gzf
#
# Read-only with respect to the program.

import argparse
import os


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("project_location")
    ap.add_argument("project_name")
    ap.add_argument("program")
    ap.add_argument("output", help="destination .gzf")
    ap.add_argument("--force", action="store_true",
                    help="overwrite an existing file")
    args = ap.parse_args()

    if os.path.exists(args.output) and not args.force:
        raise SystemExit("[!] %s exists; pass --force to replace it"
                         % args.output)

    import pyghidra
    pyghidra.start()

    from ghidra.base.project import GhidraProject
    from ghidra.app.util.exporter import GzfExporter
    from ghidra.util.task import ConsoleTaskMonitor
    from java.io import File

    monitor = ConsoleTaskMonitor()
    project = GhidraProject.openProject(args.project_location,
                                        args.project_name, True)
    try:
        path = args.program if args.program.startswith("/") \
            else "/" + args.program
        folder, _, pname = path.rpartition("/")
        program = project.openProgram(folder or "/", pname, True)
        try:
            print("[*] exporting %s ..." % pname, flush=True)
            exporter = GzfExporter()
            ok = exporter.export(File(args.output), program, None, monitor)
            if not ok:
                raise SystemExit("[!] export reported failure")
            size = os.path.getsize(args.output)
            print("[+] wrote %s (%.0f MB)" % (args.output, size / 1048576.0))
        finally:
            project.close(program)
    finally:
        project.close()


if __name__ == "__main__":
    main()
