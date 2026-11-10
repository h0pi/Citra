"""Builds the demo documents (PDF and Word) from the text sources in source/.

    python -m pip install reportlab python-docx
    python generate.py

Sources use a tiny markup: "# " title, "## " heading, "- " and "1. " lists, "**bold**", "| a | b |" table rows
and a "---" line for a page break. Output goes to documents/<bot>/<name>.pdf or .docx.
"""
import re
from pathlib import Path

from docx import Document
from docx.shared import Pt
from reportlab.lib import colors
from reportlab.lib.pagesizes import A4
from reportlab.lib.styles import ParagraphStyle
from reportlab.lib.units import cm
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.platypus import ListFlowable, PageBreak, Paragraph, SimpleDocTemplate, Spacer, Table, TableStyle

HERE = Path(__file__).parent
SOURCE = HERE / 'source'
OUTPUT = HERE / 'documents'

# Word files; everything else becomes a PDF.
WORD = {
    'returns-and-refunds', 'payment-and-invoices', 'customer-faq', 'privacy-policy', 'loyalty-programme',
    'onboarding-checklist', 'code-of-conduct', 'benefits-and-discounts',
}

def parse(text):
    """Blocks: ('title'|'heading'|'para', text), ('list', ordered, items), ('table', rows), ('break',)."""
    blocks, para, items, ordered, rows = [], [], [], False, []

    def flush():
        nonlocal para, items, rows
        if para:
            blocks.append(('para', ' '.join(para)))
        if items:
            blocks.append(('list', ordered, items))
        if rows:
            blocks.append(('table', rows))
        para, items, rows = [], [], []

    for line in text.splitlines():
        line = line.rstrip()
        numbered = re.match(r'^\d+\. (.*)', line)
        if not line:
            flush()
        elif line == '---':
            flush()
            blocks.append(('break',))
        elif line.startswith('# '):
            flush()
            blocks.append(('title', line[2:]))
        elif line.startswith('## '):
            flush()
            blocks.append(('heading', line[3:]))
        elif line.startswith('|'):
            cells = [c.strip() for c in line.strip('|').split('|')]
            if not all(re.fullmatch(r':?-+:?', c) for c in cells):  # skip "|---|---|" lines
                rows.append(cells)
        elif line.startswith('- ') or numbered:
            if para:
                flush()
            ordered = bool(numbered)
            items.append(numbered.group(1) if numbered else line[2:])
        else:
            para.append(line)
    flush()
    return blocks


def bold_runs(text):
    """('text', is_bold) pieces of a line with **bold** parts."""
    return [(part, i % 2 == 1) for i, part in enumerate(text.split('**')) if part]


def write_pdf(blocks, path):
    pdfmetrics.registerFont(TTFont('Arial', 'C:/Windows/Fonts/arial.ttf'))
    pdfmetrics.registerFont(TTFont('Arial-Bold', 'C:/Windows/Fonts/arialbd.ttf'))
    pdfmetrics.registerFontFamily('Arial', normal='Arial', bold='Arial-Bold')
    body = ParagraphStyle('body', fontName='Arial', fontSize=10.5, leading=15, spaceAfter=6)
    title = ParagraphStyle('title', parent=body, fontName='Arial-Bold', fontSize=20, leading=24, spaceAfter=10,
                           textColor=colors.HexColor('#2f4f3a'))
    heading = ParagraphStyle('heading', parent=body, fontName='Arial-Bold', fontSize=13, leading=17, spaceBefore=10)
    cell = ParagraphStyle('cell', parent=body, fontSize=9.5, leading=12, spaceAfter=0)

    def markup(text):
        return ''.join(f'<b>{t}</b>' if b else t for t, b in bold_runs(text.replace('&', '&amp;').replace('<', '&lt;')))

    story = []
    for block in blocks:
        kind = block[0]
        if kind == 'title':
            story.append(Paragraph(markup(block[1]), title))
        elif kind == 'heading':
            story.append(Paragraph(markup(block[1]), heading))
        elif kind == 'para':
            story.append(Paragraph(markup(block[1]), body))
        elif kind == 'list':
            story.append(ListFlowable([Paragraph(markup(i), body) for i in block[2]],
                                      bulletType='1' if block[1] else 'bullet', leftIndent=14, bulletFontName='Arial'))
        elif kind == 'table':
            data = [[Paragraph(markup(c), cell) for c in row] for row in block[1]]
            table = Table(data, repeatRows=1, hAlign='LEFT')
            table.setStyle(TableStyle([
                ('BACKGROUND', (0, 0), (-1, 0), colors.HexColor('#e7e0d3')),
                ('GRID', (0, 0), (-1, -1), 0.5, colors.HexColor('#b5ab9a')),
                ('VALIGN', (0, 0), (-1, -1), 'TOP'),
            ]))
            story += [table, Spacer(1, 8)]
        elif kind == 'break':
            story.append(PageBreak())

    # No page header or footer: text repeated on every page would end up in every searchable passage.
    SimpleDocTemplate(str(path), pagesize=A4, leftMargin=2 * cm, rightMargin=2 * cm, topMargin=2 * cm,
                      bottomMargin=2 * cm, title=blocks[0][1], author='Northwind Outdoor d.o.o.').build(story)


def write_docx(blocks, path):
    doc = Document()
    doc.styles['Normal'].font.name = 'Arial'
    doc.styles['Normal'].font.size = Pt(10.5)
    doc.core_properties.title = blocks[0][1]
    doc.core_properties.author = 'Northwind Outdoor d.o.o.'

    def runs(paragraph, text):
        for t, b in bold_runs(text):
            paragraph.add_run(t).bold = b

    for block in blocks:
        kind = block[0]
        if kind == 'title':
            doc.add_heading(block[1], level=0)
        elif kind == 'heading':
            doc.add_heading(block[1], level=1)
        elif kind == 'para':
            runs(doc.add_paragraph(), block[1])
        elif kind == 'list':
            for item in block[2]:
                runs(doc.add_paragraph(style='List Number' if block[1] else 'List Bullet'), item)
        elif kind == 'table':
            rows = block[1]
            table = doc.add_table(rows=len(rows), cols=len(rows[0]), style='Table Grid')
            for r, row in enumerate(rows):
                for c, text in enumerate(row):
                    table.cell(r, c).text = text
                    if r == 0:
                        table.cell(r, c).paragraphs[0].runs[0].bold = True
        elif kind == 'break':
            doc.add_page_break()
    doc.save(str(path))


def main():
    count = 0
    for source in sorted(SOURCE.glob('*/*.md')):
        bot, name = source.parent.name, source.stem
        folder = OUTPUT / bot
        folder.mkdir(parents=True, exist_ok=True)
        blocks = parse(source.read_text(encoding='utf-8'))
        if name in WORD:
            write_docx(blocks, folder / f'{name}.docx')
        else:
            write_pdf(blocks, folder / f'{name}.pdf')
        count += 1
    print(f'{count} documents written to {OUTPUT}')


if __name__ == '__main__':
    main()
