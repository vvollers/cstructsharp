/// <reference types="node" />
import { Buffer } from "node:buffer";

export function inspectionFiles(): Record<string, Buffer> {
  const chunk = (tag: string, data: Buffer) => {
    const b = Buffer.alloc(data.length + 12);
    b.writeUInt32BE(data.length);
    b.write(tag, 4);
    data.copy(b, 8);
    return b;
  };
  const ihdr = Buffer.alloc(13);
  ihdr.writeUInt32BE(320);
  ihdr.writeUInt32BE(240, 4);
  ihdr[8] = 8;
  ihdr[9] = 2;
  const gamma = Buffer.alloc(4);
  gamma.writeUInt32BE(45455);
  const png = Buffer.concat([
    Buffer.from("89504e470d0a1a0a", "hex"),
    chunk("IHDR", ihdr),
    chunk("IDAT", Buffer.alloc(70000)),
    chunk("gAMA", gamma),
    chunk("PLTE", Buffer.from([10, 20, 30])),
    chunk("iTXt", Buffer.from("Title\0\0\0en\0Title\0Hello PNG", "utf8")),
    chunk("IEND", Buffer.alloc(0)),
  ]);
  const segment = (marker: number, data: Buffer) => {
    const b = Buffer.alloc(4 + data.length);
    b[0] = 255;
    b[1] = marker;
    b.writeUInt16BE(data.length + 2, 2);
    data.copy(b, 4);
    return b;
  };
  const jpg = Buffer.concat([
    Buffer.from([255, 216]),
    segment(0xdb, Buffer.concat([Buffer.from([0]), Buffer.alloc(64, 7)])),
    segment(
      0xe1,
      Buffer.from(
        "45786966000049492a000800000001001a010500010000001a000000000000004800000001000000",
        "hex",
      ),
    ),
    segment(0xc2, Buffer.from([8, 0, 240, 1, 64, 1, 1, 0x21, 0])),
    segment(0xda, Buffer.from([1, 1, 0, 0, 0, 0])),
    Buffer.alloc(70000, 17),
    Buffer.from([255, 0, 255, 208]),
    segment(0xda, Buffer.from([1, 1, 0, 1, 63, 0])),
    Buffer.from([32, 255, 0, 32, 255, 217]),
  ]);
  const local = Buffer.alloc(31);
  local.writeUInt32LE(0x04034b50);
  local.writeUInt16LE(20, 4);
  local.writeUInt16LE(8, 6);
  local.writeUInt16LE(1, 26);
  local[30] = 97;
  const descriptor = Buffer.alloc(16);
  descriptor.writeUInt32LE(0x08074b50);
  descriptor.writeUInt32LE(70000, 8);
  descriptor.writeUInt32LE(70000, 12);
  const central = Buffer.alloc(47);
  central.writeUInt32LE(0x02014b50);
  central.writeUInt16LE(20, 6);
  central.writeUInt16LE(8, 8);
  central.writeUInt32LE(70000, 20);
  central.writeUInt32LE(70000, 24);
  central.writeUInt16LE(1, 28);
  central[46] = 97;
  const footer = Buffer.alloc(22);
  footer.writeUInt32LE(0x06054b50);
  footer.writeUInt16LE(1, 8);
  footer.writeUInt16LE(1, 10);
  footer.writeUInt32LE(47, 12);
  footer.writeUInt32LE(31 + 70000 + 16, 16);
  const zip = Buffer.concat([local, Buffer.alloc(70000), descriptor, central, footer]);
  const exe = Buffer.alloc(72000);
  exe.write("MZ");
  exe.writeUInt32LE(66000, 60);
  exe.write("PE\0\0", 66000);
  exe.writeUInt16LE(0x14c, 66004);
  exe.writeUInt16LE(1, 66006);
  exe.writeUInt16LE(224, 66020);
  const opt = 66024;
  exe.writeUInt16LE(0x10b, opt);
  exe.writeUInt32LE(512, opt + 60);
  exe.writeUInt32LE(16, opt + 92);
  exe.writeUInt32LE(0x1000, opt + 104);
  exe.writeUInt32LE(40, opt + 108);
  const sh = opt + 224;
  exe.write(".rdata", sh);
  exe.writeUInt32LE(1000, sh + 8);
  exe.writeUInt32LE(0x1000, sh + 12);
  exe.writeUInt32LE(1000, sh + 16);
  exe.writeUInt32LE(70000, sh + 20);
  exe.writeUInt32LE(0x1080, 70000);
  exe.writeUInt32LE(0x1060, 70012);
  exe.writeUInt32LE(0x1080, 70016);
  exe.write("KERNEL32.dll\0", 70096);
  exe.writeUInt32LE(0x10a0, 70128);
  exe.writeUInt16LE(42, 70160);
  exe.write("ExitProcess\0", 70162);
  exe.writeUInt32LE(0x10c8, opt + 96 + 14 * 8);
  exe.writeUInt32LE(72, opt + 100 + 14 * 8);
  exe.writeUInt32LE(72, 70200);
  exe.writeUInt32LE(0x112c, 70208);
  exe.writeUInt32LE(200, 70212);
  exe.write("BSJB", 70300);
  exe.writeUInt32LE(12, 70312);
  exe.write("v4.0.30319\0", 70316);
  exe.writeUInt16LE(1, 70330);
  exe.writeUInt32LE(64, 70332);
  exe.writeUInt32LE(32, 70336);
  exe.write("#~\0", 70340);
  exe.writeBigUInt64LE(1n, 70372);
  exe.writeUInt32LE(3, 70388);
  exe.writeUInt32LE(0x1226, opt + 96 + 6 * 8);
  exe.writeUInt32LE(28, opt + 100 + 6 * 8);
  exe.writeUInt32LE(2, 70562);
  exe.writeUInt32LE(40, 70566);
  exe.writeUInt32LE(70600, 70574);
  exe.write("RSDS", 70600);
  exe.writeUInt32LE(7, 70620);
  exe.write("sample.pdb\0", 70624);
  exe.writeUInt32LE(0x12bc, opt + 96 + 2 * 8);
  exe.writeUInt32LE(128, opt + 100 + 2 * 8);
  exe.writeUInt16LE(1, 70714);
  exe.writeUInt32LE(16, 70716);
  exe.writeUInt32LE(24, 70720);
  exe.writeUInt32LE(0x1334, 70724);
  exe.writeUInt32LE(8, 70728);
  exe.writeUInt32LE(71100, opt + 96 + 4 * 8);
  exe.writeUInt32LE(16, opt + 100 + 4 * 8);
  exe.writeUInt32LE(16, 71100);
  exe.writeUInt16LE(0x200, 71104);
  exe.writeUInt16LE(2, 71106);
  const elf = Buffer.alloc(70200);
  elf.write("\x7fELF");
  elf[4] = 2;
  elf[5] = 1;
  elf.writeBigUInt64LE(66000n, 40);
  elf.writeUInt16LE(64, 58);
  elf.writeUInt16LE(3, 60);
  elf.writeUInt16LE(1, 62);
  elf.writeUInt32LE(3, 66068);
  elf.writeBigUInt64LE(70000n, 66088);
  elf.writeBigUInt64LE(32n, 66096);
  elf.writeUInt32LE(11, 66132);
  elf.writeBigUInt64LE(70100n, 66152);
  elf.writeBigUInt64LE(24n, 66160);
  elf.writeUInt32LE(1, 66168);
  elf.writeBigUInt64LE(24n, 66184);
  elf.write("\0section\0function\0", 70000);
  elf.writeUInt32LE(9, 70100);
  elf[70104] = 0x12;
  elf.writeBigUInt64LE(4096n, 70108);
  let pdfText = "%PDF-1.7\n" + "%" + " ".repeat(70000) + "\n";
  const obj = pdfText.length;
  pdfText += "1 0 obj\n<< /Type /Catalog /Pages 2 0 R /Note (literal >> text) >>\nendobj\n";
  const stream = pdfText.length;
  pdfText += "2 0 obj\n<< /Length 5 >>\nstream\nhello\nendstream\nendobj\n";
  const xref = pdfText.length;
  pdfText += `xref\n0 3\n0000000000 65535 f \n${String(obj).padStart(10, "0")} 00000 n \n${String(stream).padStart(10, "0")} 00000 n \ntrailer\n<< /Size 3 /Root 1 0 R >>\nstartxref\n${xref}\n%%EOF\n`;
  return { png, jpg, zip, exe, elf, pdf: Buffer.from(pdfText) };
}

export function inspectionVariants(): {
  name: string;
  ext: string;
  bytes: Buffer;
  expected: string;
}[] {
  const { zip, exe, elf, pdf } = inspectionFiles();
  const zipEnd = zip!.length - 22,
    directory = zip!.readUInt32LE(zipEnd + 16),
    central = Buffer.from(zip!.subarray(directory, zipEnd));
  central.writeUInt32LE(0xffffffff, 20);
  central.writeUInt32LE(0xffffffff, 24);
  central.writeUInt16LE(20, 30);
  const extra = Buffer.alloc(20);
  extra.writeUInt16LE(1);
  extra.writeUInt16LE(16, 2);
  extra.writeBigUInt64LE(70000n, 4);
  extra.writeBigUInt64LE(70000n, 12);
  const record = Buffer.alloc(56);
  record.writeUInt32LE(0x06064b50);
  record.writeBigUInt64LE(44n, 4);
  record.writeBigUInt64LE(1n, 24);
  record.writeBigUInt64LE(1n, 32);
  record.writeBigUInt64LE(BigInt(central.length + extra.length), 40);
  record.writeBigUInt64LE(BigInt(directory), 48);
  const locator = Buffer.alloc(20);
  locator.writeUInt32LE(0x07064b50);
  locator.writeBigUInt64LE(BigInt(zipEnd + 20), 8);
  locator.writeUInt32LE(1, 16);
  const end = Buffer.from(zip!.subarray(zipEnd));
  end.writeUInt16LE(65535, 10);
  end.writeUInt32LE(0xffffffff, 16);
  // ZIP64 entries use 64-bit data descriptor sizes too.
  const body = Buffer.from(zip!.subarray(0, directory));
  body.writeUInt16LE(0, 6);
  central.writeUInt16LE(0, 8);
  const zip64 = Buffer.concat([body, central, extra, record, locator, end]);
  const pe64 = Buffer.from(exe!);
  const opt = 66024;
  exe!.copy(pe64, opt + 112, opt + 96, opt + 224);
  exe!.copy(pe64, opt + 240, opt + 224, opt + 264);
  pe64.writeUInt16LE(240, 66020);
  pe64.writeUInt16LE(0x8664, 66004);
  pe64.writeUInt16LE(0x20b, opt);
  pe64.writeUInt32LE(16, opt + 108);
  pe64.writeBigUInt64LE(0x140000000n, opt + 24);
  pe64.writeBigUInt64LE(0x10a0n, 70128);
  const elfBe = Buffer.from(elf!);
  elfBe[5] = 2;
  elfBe.writeBigUInt64BE(66000n, 40);
  elfBe.writeUInt16BE(64, 58);
  elfBe.writeUInt16BE(0, 60);
  elfBe.writeUInt16BE(65535, 62);
  elfBe.writeBigUInt64BE(3n, 66032);
  elfBe.writeUInt32BE(1, 66040);
  for (const p of [66064, 66128]) {
    elfBe.writeUInt32BE(elf!.readUInt32LE(p + 4), p + 4);
    for (const o of [24, 32, 56]) elfBe.writeBigUInt64BE(elf!.readBigUInt64LE(p + o), p + o);
    elfBe.writeUInt32BE(elf!.readUInt32LE(p + 40), p + 40);
  }
  elfBe.writeUInt32BE(9, 70100);
  elfBe.writeBigUInt64BE(4096n, 70108);
  const old = pdf!.toString(),
    prev = Number(/startxref\s+(\d+)/.exec(old)![1]),
    newObject = pdf!.length;
  const update = "1 0 obj\n<< /Type /Catalog /Note (new revision) >>\nendobj\n",
    xref = newObject + update.length;
  const incremental = Buffer.concat([
    pdf!,
    Buffer.from(
      `${update}xref\n1 1\n${String(newObject).padStart(10, "0")} 00000 n \ntrailer\n<< /Size 3 /Root 1 0 R /Prev ${prev} >>\nstartxref\n${xref}\n%%EOF\n`,
    ),
  ]);
  return [
    { name: "ZIP64", ext: "zip", bytes: zip64, expected: "zip64_end" },
    { name: "PE32+", ext: "exe", bytes: pe64, expected: "5368709120" },
    { name: "big-endian ELF extended counts", ext: "elf", bytes: elfBe, expected: "function" },
    { name: "incremental PDF", ext: "pdf", bytes: incremental, expected: "new revision" },
  ];
}
