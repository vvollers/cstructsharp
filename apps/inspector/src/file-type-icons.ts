import fileIcon from "@iconify-icons/vscode-icons/default-file";
import image from "@iconify-icons/vscode-icons/file-type-image";
import audio from "@iconify-icons/vscode-icons/file-type-audio";
import video from "@iconify-icons/vscode-icons/file-type-video";
import archive from "@iconify-icons/vscode-icons/file-type-zip";
import binary from "@iconify-icons/vscode-icons/file-type-binary";
import font from "@iconify-icons/vscode-icons/file-type-font";
import database from "@iconify-icons/vscode-icons/file-type-db";
import sqlite from "@iconify-icons/vscode-icons/file-type-sqlite";
import word from "@iconify-icons/vscode-icons/file-type-word";
import spreadsheet from "@iconify-icons/vscode-icons/file-type-excel";
import presentation from "@iconify-icons/vscode-icons/file-type-powerpoint";
import pdf from "@iconify-icons/vscode-icons/file-type-pdf2";
import ebook from "@iconify-icons/vscode-icons/file-type-epub";
import drawing from "@iconify-icons/vscode-icons/file-type-libreoffice-draw";
import model from "@iconify-icons/vscode-icons/file-type-openscad";
import gltf from "@iconify-icons/vscode-icons/file-type-gltf";
import fbx from "@iconify-icons/vscode-icons/file-type-fbx";
import blender from "@iconify-icons/vscode-icons/file-type-blender";
import photoshop from "@iconify-icons/vscode-icons/file-type-photoshop";
import gimp from "@iconify-icons/vscode-icons/file-type-gimp";
import xml from "@iconify-icons/vscode-icons/file-type-xml";
import text from "@iconify-icons/vscode-icons/file-type-text";
import config from "@iconify-icons/vscode-icons/file-type-config";
import outlook from "@iconify-icons/vscode-icons/file-type-outlook";
import encryption from "@iconify-icons/vscode-icons/file-type-gpg";
import network from "@iconify-icons/vscode-icons/file-type-http";
import map from "@iconify-icons/vscode-icons/file-type-map";
import wasm from "@iconify-icons/vscode-icons/file-type-wasm";
import java from "@iconify-icons/vscode-icons/file-type-java";
import flash from "@iconify-icons/vscode-icons/file-type-flash";
import packageIcon from "@iconify-icons/vscode-icons/file-type-package";

interface FilePresentation {
  icon: typeof fileIcon;
  family: string;
}

// Curated by format identity rather than schema/container title. Sources and family
// substitutions are documented in FILE-ICONS.md. All icon data is bundled offline.
export const fileTypeIcons: Record<string, FilePresentation> = {};
function assign(extensions: string, icon: typeof fileIcon, family: string): void {
  for (const extension of extensions.split(" ")) {
    if (fileTypeIcons[extension]) throw new Error(`Duplicate file icon: ${extension}`);
    fileTypeIcons[extension] = { icon, family };
  }
}

assign(
  "jpg png apng gif webp flif cr2 cr3 orf arw dng nef rw2 raf tif bmp icns jxr ico cur heic avif ktx bpg j2c jp2 jpm jpx jxl jls",
  image,
  "Image / camera RAW",
);
assign("psd", photoshop, "Photoshop image");
assign("xcf", gimp, "GIMP image");
assign("dcm", image, "Medical imaging");
assign(
  "aac ac3 amr ape aif dsf flac it mid mpc mp1 mp2 mp3 m4a m4b m4p f4a f4b oga ogg opus qcp s3m spx voc wav wv xm",
  audio,
  "Audio / music",
);
assign(
  "3gp 3g2 asf avi f4v f4p flv mj2 mkv mov mp4 mpg mts m4v mxf ogm ogv ogx rm webm",
  video,
  "Video / media container",
);
assign("swf", flash, "Flash animation");
assign(
  "zip tar rar gz bz2 7z xz Z lz lz4 zst lzh arj cpio ace ar cab tar.gz",
  archive,
  "Archive / compression",
);
assign("apk crx xpi deb rpm asar", packageIcon, "Application package");
assign("dmg iso", packageIcon, "Disk image");
assign("elf macho exe dll nes", binary, "Executable / binary program");
assign("wasm", wasm, "WebAssembly program");
assign("class jar", java, "Java program");
assign("woff woff2 eot ttf otf ttc", font, "Font");
assign("sqlite", sqlite, "SQLite database");
assign("arrow parquet avro jmp sav", database, "Dataset / statistical table");
assign("cfb", archive, "Compound document container");
assign("docx docm dotx dotm odt ott rtf pages", word, "Text document");
assign("xlsx xlsm xltx xltm ods ots numbers", spreadsheet, "Spreadsheet");
assign("pptx pptm potx potm ppsx ppsm odp otp key", presentation, "Presentation");
assign("pdf ps eps", pdf, "Page description / print document");
assign("epub mobi chm", ebook, "E-book / help document");
assign("odg otg vsdx indd", drawing, "Drawing / page layout");
assign("dwg skp stl drc 3mf", model, "CAD / 3D model");
assign("glb", gltf, "glTF 3D model");
assign("fbx", fbx, "FBX 3D model");
assign("blend", blender, "Blender 3D scene");
assign("shp", map, "GIS / geospatial data");
assign("xml", xml, "XML document");
assign("vtt", text, "Subtitle / timed text");
assign("icc", config, "Color profile");
assign("mie", config, "Metadata");
assign("dat reg", config, "Windows registry");
assign("lnk alias", config, "Shortcut / bookmark");
assign("pst", outlook, "Email archive");
assign("ics vcf", outlook, "Calendar / contacts");
assign("pgp", encryption, "Encrypted / signed data");
assign("pcap", network, "Network packet capture");

export function filePresentation(extension: string): FilePresentation {
  // Preserve the case-sensitive Unix-compress extension Z.
  return (
    fileTypeIcons[extension] ??
    fileTypeIcons[extension.toLowerCase()] ?? { icon: fileIcon, family: "File" }
  );
}
