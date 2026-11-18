// node scripts/export-icon.cjs [absolute path to the sharp package]
// Exports all platform assets from the editable SVG; no network access needed.
const fs = require('node:fs');
const path = require('node:path');
const sharp = require(process.argv[2] || 'sharp');
const root = path.resolve(__dirname, '..');
const svg = fs.readFileSync(path.join(root, 'assets/window-tiler.svg'), 'utf8');
const output = path.join(root, 'artifacts/icon');
const app = path.join(root, 'src/WindowTiler.App');
fs.mkdirSync(output, {recursive: true});
fs.mkdirSync(path.join(app, 'Assets'), {recursive: true});
(async () => {
  const sizes = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256];
  const frames = [];
  for (const size of sizes) {
    const source = size <= 24 ? svg.replace('<g class="detail"', '<g display="none" class="detail"') : svg;
    const png = await sharp(Buffer.from(source)).resize(size, size).png().toBuffer();
    frames.push(png);
    fs.writeFileSync(path.join(output, `icon-${size}.png`), png);
  }
  const header = Buffer.alloc(6 + sizes.length * 16);
  header.writeUInt16LE(1, 2); header.writeUInt16LE(sizes.length, 4);
  let offset = header.length;
  sizes.forEach((size, i) => {
    const entry = 6 + i * 16;
    header[entry] = header[entry + 1] = size === 256 ? 0 : size;
    header.writeUInt16LE(1, entry + 4); header.writeUInt16LE(32, entry + 6);
    header.writeUInt32LE(frames[i].length, entry + 8); header.writeUInt32LE(offset, entry + 12);
    offset += frames[i].length;
  });
  fs.writeFileSync(path.join(app, 'app.ico'), Buffer.concat([header, ...frames]));
  fs.writeFileSync(path.join(app, 'Assets/app-icon.png'), frames.at(-1));
  await sharp(Buffer.from(svg)).png().toFile(path.join(output, 'icon-master.png'));
  const panels = [];
  for (const [row, background] of ['#F3F5FA', '#10151F'].entries()) {
    panels.push({input: await sharp({create: {width: 760, height: 184, channels: 4, background}}).png().toBuffer(), left: 0, top: row * 184});
    let x = 28;
    for (const size of [16, 24, 32, 48, 64, 128]) {
      panels.push({input: frames[sizes.indexOf(size)], left: x, top: row * 184 + 24});
      x += size + 32;
    }
  }
  await sharp({create: {width: 760, height: 368, channels: 4, background: '#FFFFFF'}}).composite(panels).png().toFile(path.join(output, 'icon-sizes.png'));
  console.log('Exported SVG, multi-resolution ICO, app PNG, and preview sheets.');
})().catch(error => { console.error(error); process.exitCode = 1; });
