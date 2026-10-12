// Dumps the Havok 5.5 class registry compiled into Dunia.dll as JackAll's HavokClasses.json.
// Each hkClass comes from one 12-argument constructor call; enum pointers the member tables leave
// null are recovered from the startup code that stores them. Of same-named classes the lowest-addressed
// is live, the rest being compat copies for older packfiles.
// Run with Dunia.dll open (GOG or Steam); the argument is the output path. Read-only.
// @category FC2RE

import ghidra.app.script.GhidraScript;
import ghidra.app.util.PseudoDisassembler;
import ghidra.app.util.PseudoInstruction;
import ghidra.program.model.lang.Register;
import ghidra.program.model.listing.Instruction;
import ghidra.program.model.mem.Memory;
import ghidra.program.model.mem.MemoryBlock;
import ghidra.program.model.scalar.Scalar;
import ghidra.program.model.symbol.Reference;
import java.io.FileWriter;
import java.io.PrintWriter;
import java.util.*;

public class DumpHavokClasses extends GhidraScript {
    // hkClass::hkClass(name, parent, size, interfaces, numInterfaces, enums, numEnums,
    //                  members, numMembers, defaults, attributes, flags)
    private static final long HKCLASS_CTOR_GOG = 0x10ae7e80L;
    private static final int MEMBER_SIZE = 0x18, ENUM_SIZE = 0x14;

    private Memory mem;
    private final Map<Long, String> enumIds = new LinkedHashMap<>();

    @Override
    public void run() throws Exception {
        mem = currentProgram.getMemory();
        long ctor = getScriptArgs().length > 1 ? Long.decode(getScriptArgs()[1]) : HKCLASS_CTOR_GOG;

        TreeMap<Long, long[]> byAddress = new TreeMap<>();
        for (long[] args : findConstructorCalls(ctor)) byAddress.put(args[0], args);
        Map<String, long[]> live = new LinkedHashMap<>();
        Map<Long, String> classNames = new HashMap<>();
        for (long[] c : byAddress.values()) {
            classNames.put(c[0], cstr(c[1]));
            live.putIfAbsent(cstr(c[1]), c);
        }
        for (long[] c : live.values())
            for (int e = 0; e < c[7]; e++) enumIds.put(c[6] + (long) e * ENUM_SIZE, cstr(c[1]) + "." + cstr(u32(c[6] + (long) e * ENUM_SIZE)));

        StringBuilder classes = new StringBuilder();
        for (long[] c : live.values()) {
            if (classes.length() > 0) classes.append(",\n");
            classes.append("    {\"name\": ").append(js(cstr(c[1])))
                .append(", \"parent\": ").append(js(c[2] == 0 ? null : classNames.get(c[2])))
                .append(", \"size\": ").append(c[3]).append(", \"interfaces\": ").append(c[5]).append(", \"enums\": [");
            for (int e = 0; e < c[7]; e++) classes.append(e > 0 ? ", " : "").append(js(enumIds.get(c[6] + (long) e * ENUM_SIZE)));
            classes.append("], \"members\": [");
            for (int m = 0; m < c[9]; m++) {
                long at = c[8] + (long) m * MEMBER_SIZE;
                long cls = u32(at + 4);
                long en = enumOf(at);
                classes.append(m > 0 ? ",\n      " : "\n      ")
                    .append("{\"name\": ").append(js(cstr(u32(at))))
                    .append(", \"type\": ").append(u8(at + 12)).append(", \"subtype\": ").append(u8(at + 13))
                    .append(", \"class\": ").append(js(cls == 0 ? null : classNames.get(cls)))
                    .append(", \"enum\": ").append(js(en == 0 ? null : enumId(en)))
                    .append(", \"count\": ").append(mem.getShort(toAddr(at + 14)))
                    .append(", \"flags\": ").append(mem.getShort(toAddr(at + 16)) & 0xffff)
                    .append(", \"offset\": ").append(mem.getShort(toAddr(at + 18)) & 0xffff).append("}");
            }
            classes.append("]}");
        }

        StringBuilder enums = new StringBuilder();
        for (Map.Entry<Long, String> e : enumIds.entrySet()) {
            long at = e.getKey();
            if (enums.length() > 0) enums.append(",\n");
            enums.append("    ").append(js(e.getValue())).append(": {\"name\": ").append(js(cstr(u32(at)))).append(", \"items\": [");
            long items = u32(at + 4);
            for (int k = 0; k < u32(at + 8); k++)
                enums.append(k > 0 ? ", " : "").append("[").append(mem.getInt(toAddr(items + k * 8L))).append(", ").append(js(cstr(u32(items + k * 8L + 4)))).append("]");
            enums.append("]}");
        }

        try (PrintWriter w = new PrintWriter(new FileWriter(getScriptArgs()[0]))) {
            w.println("{\n  \"version\": \"Havok-5.5.0-r1\",\n  \"enums\": {\n" + enums + "\n  },\n  \"classes\": [\n" + classes + "\n  ]\n}");
        }
        println("classes " + live.size() + ", enums " + enumIds.size());
    }

    // Every `MOV ECX, self; CALL ctor` preceded by exactly 12 constant pushes, as {self, args...}.
    private List<long[]> findConstructorCalls(long ctor) throws Exception {
        PseudoDisassembler pd = new PseudoDisassembler(currentProgram);
        MemoryBlock text = mem.getBlock(".text");
        long base = text.getStart().getOffset();
        byte[] buf = new byte[(int) text.getSize()];
        text.getBytes(text.getStart(), buf);
        List<long[]> calls = new ArrayList<>();
        for (int i = 5; i + 5 <= buf.length; i++) {
            if ((buf[i] & 0xff) != 0xE8 || (buf[i - 5] & 0xff) != 0xB9) continue;
            int rel = (buf[i + 1] & 0xff) | (buf[i + 2] & 0xff) << 8 | (buf[i + 3] & 0xff) << 16 | (buf[i + 4] & 0xff) << 24;
            long call = base + i;
            if (call + 5 + rel != ctor) continue;
            long mov = call - 5;
            for (long start = mov - 60; start <= mov - 24; start++) {
                List<Long> pushes = pushesBetween(pd, start, mov);
                int before = u8(start - 1);
                if (pushes == null || pushes.size() != 12 || (before != 0xCC && before != 0xC3 && before != 0x90)) continue;
                long[] args = new long[13];
                args[0] = u32(call - 4);
                for (int k = 0; k < 12; k++) args[1 + k] = pushes.get(11 - k);
                calls.add(args);
                break;
            }
        }
        return calls;
    }

    private List<Long> pushesBetween(PseudoDisassembler pd, long start, long end) {
        List<Long> pushes = new ArrayList<>();
        for (long a = start; a < end; ) {
            PseudoInstruction ins;
            try {
                ins = pd.disassemble(toAddr(a));
            } catch (Exception e) {
                return null;
            }
            if (ins == null || !ins.getMnemonicString().equals("PUSH") || ins.getScalar(0) == null) return null;
            pushes.add(ins.getScalar(0).getUnsignedValue() & 0xffffffffL);
            a += ins.getLength();
            if (a > end) return null;
        }
        return pushes;
    }

    // A member's enum, from the static table or from the startup code that stores it.
    private long enumOf(long member) throws Exception {
        int type = u8(member + 12);
        long slot = member + 8, stat = u32(slot);
        if (stat != 0 || (type != 24 && type != 31)) return stat;
        for (Reference w : getReferencesTo(toAddr(slot))) {
            Instruction store = getInstructionAt(w.getFromAddress());
            if (store == null || !w.getReferenceType().isWrite()) continue;
            Scalar imm = store.getScalar(1);
            if (imm != null) return imm.getUnsignedValue();
            Register src = store.getRegister(1);
            Instruction p = store.getPrevious();
            for (int back = 0; src != null && back < 16 && p != null; back++, p = p.getPrevious()) {
                if (!src.equals(p.getRegister(0))) continue;
                if (!p.getMnemonicString().equals("MOV")) break;
                if (p.getScalar(1) != null) return p.getScalar(1).getUnsignedValue();
                for (Reference r : p.getReferencesFrom())
                    if (r.getReferenceType().isRead()) return u32(r.getToAddress().getOffset());
                break;
            }
        }
        return 0;
    }

    private String enumId(long at) throws Exception {
        String id = enumIds.get(at);
        if (id == null) {
            id = cstr(u32(at));
            enumIds.put(at, id);
        }
        return id;
    }

    private long u32(long a) throws Exception {
        return mem.getInt(toAddr(a)) & 0xffffffffL;
    }

    private int u8(long a) throws Exception {
        return mem.getByte(toAddr(a)) & 0xff;
    }

    private String cstr(long a) throws Exception {
        if (a == 0) return null;
        StringBuilder sb = new StringBuilder();
        for (int c; (c = u8(a + sb.length())) != 0 && sb.length() < 256; ) sb.append((char) c);
        return sb.toString();
    }

    private static String js(String s) {
        return s == null ? "null" : "\"" + s.replace("\\", "\\\\").replace("\"", "\\\"") + "\"";
    }
}
