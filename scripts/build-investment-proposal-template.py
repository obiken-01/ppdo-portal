"""Builds backend/PPDO.Infrastructure/Templates/InvestmentProposalTemplate.docx (PPDO-158).

Source: the province's "PGOM-Proposal_Template_Updated-1.26.2026.docx" (kept outside this public
repo). Keeps its letterhead header, "Page X of Y" footer, page setup and styles; empties the body
(the export writes it); compresses the 2.1 MB seal; sets Verdana 10 pt as the document default;
and drops author names and the Grammarly id from the document properties.

Usage: python scripts/build-investment-proposal-template.py <path-to-province-template.docx>
Needs Pillow. Re-run only when the province changes its letterhead.
"""
import io, re, sys, zipfile
from PIL import Image

SRC = sys.argv[1]
DST = "backend/PPDO.Infrastructure/Templates/InvestmentProposalTemplate.docx"

src = zipfile.ZipFile(SRC)
out = io.BytesIO()
dst = zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED)

for info in src.infolist():
    name, data = info.filename, src.read(info.filename)
    if name == "docProps/custom.xml":
        continue
    if name == "[Content_Types].xml":
        data = re.sub(rb'<Override PartName="/docProps/custom.xml"[^>]*/>', b"", data)
    elif name == "_rels/.rels":
        data = re.sub(rb'<Relationship [^>]*Target="docProps/custom.xml"/>', b"", data)
    elif name == "docProps/core.xml":
        data = re.sub(rb"<dc:creator>.*?</dc:creator>", b"<dc:creator>PPDO Portal</dc:creator>", data)
        data = re.sub(rb"<cp:lastModifiedBy>.*?</cp:lastModifiedBy>", b"<cp:lastModifiedBy>PPDO Portal</cp:lastModifiedBy>", data)
        data = re.sub(rb"<cp:lastPrinted>.*?</cp:lastPrinted>", b"", data)
    elif name == "word/media/image1.png":
        # The seal prints 1 inch wide; 300 px is 300 dpi.
        im = Image.open(io.BytesIO(data))
        im.thumbnail((300, 300), Image.LANCZOS)
        buf = io.BytesIO()
        im.save(buf, "PNG", optimize=True)
        data = buf.getvalue()
    elif name == "word/styles.xml":
        data = re.sub(
            rb"<w:docDefaults>.*?</w:docDefaults>",
            b'<w:docDefaults><w:rPrDefault><w:rPr><w:rFonts w:ascii="Verdana" w:eastAsia="Verdana" w:hAnsi="Verdana" w:cs="Verdana"/>'
            b'<w:sz w:val="20"/><w:szCs w:val="20"/><w:lang w:val="en-US" w:eastAsia="en-US" w:bidi="ar-SA"/></w:rPr></w:rPrDefault>'
            b'<w:pPrDefault><w:pPr><w:spacing w:after="0" w:line="240" w:lineRule="auto"/></w:pPr></w:pPrDefault></w:docDefaults>',
            data, flags=re.S)
    elif name == "word/document.xml":
        sect = re.findall(rb"<w:sectPr.*?</w:sectPr>", data, re.S)[-1]
        data = re.sub(rb"<w:body>.*</w:body>", b"<w:body><w:p/>" + sect + b"</w:body>", data, flags=re.S)
    dst.writestr(info, data)

dst.close()
open(DST, "wb").write(out.getvalue())
print(DST, len(out.getvalue()), "bytes")
