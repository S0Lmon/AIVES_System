"""Generate the editable architecture diagram and its SVG/PNG exports.

Run from any directory: python scripts/render_architecture.py
Requires Pillow. Labels reflect Razor Pages and SignalR on main.
"""
from pathlib import Path
import math
import xml.etree.ElementTree as ET
from html import escape
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'docs'
W, H = 2400, 1770
BG, FG, MUTED = '#101318', '#edf1f7', '#b4bece'
BLUE, GREEN, GOLD, PURPLE = '#689bff', '#48c59b', '#edb340', '#b58cfa'
im = Image.new('RGB', (W, H), BG)
draw = ImageDraw.Draw(im)
svg = [f'<svg xmlns="http://www.w3.org/2000/svg" width="{W}" height="{H}" viewBox="0 0 {W} {H}">', f'<rect width="{W}" height="{H}" fill="{BG}"/>']
mx = ET.Element('mxfile', host='app.diagrams.net')
diagram = ET.SubElement(mx, 'diagram', name='AIVES Architecture', id='aives-architecture')
model = ET.SubElement(diagram, 'mxGraphModel', page='1', pageWidth=str(W), pageHeight=str(H), background=BG)
root = ET.SubElement(model, 'root')
ET.SubElement(root, 'mxCell', id='0')
ET.SubElement(root, 'mxCell', id='1', parent='0')
counter = 1


def cell(value, style, x, y, w, h, edge=False):
    global counter
    counter += 1
    attrs = dict(id=str(counter), value=value, style=style, parent='1')
    attrs['edge' if edge else 'vertex'] = '1'
    c = ET.SubElement(root, 'mxCell', **attrs)
    g = ET.SubElement(c, 'mxGeometry', x=str(x), y=str(y), width=str(w), height=str(h), **{'as': 'geometry'})
    return g


def font(size, bold=False):
    return ImageFont.truetype('C:/Windows/Fonts/' + ('segoeuib.ttf' if bold else 'segoeui.ttf'), size)


def text(x, y, value, size=21, color=MUTED, bold=False):
    # y is the top of a line, shared across the three outputs.
    f = font(size, bold)
    assert draw.textlength(value, font=f) < W - 40, value
    draw.text((x, y), value, font=f, fill=color, anchor='mt')
    svg.append(f'<text x="{x}" y="{y + size}" text-anchor="middle" font-family="Segoe UI,Arial,sans-serif" font-size="{size}" font-weight="{700 if bold else 400}" fill="{color}">{escape(value)}</text>')
    width = math.ceil(draw.textlength(value, font=f)) + 32
    cell(value, f'text;html=0;align=center;verticalAlign=top;whiteSpace=wrap;overflow=hidden;fontFamily=Segoe UI;fontSize={size};fontColor={color};fontStyle={1 if bold else 0};', x-width/2, y-2, width, size+12)


def rect(x, y, w, h, stroke, fill=BG, dashed=False, radius=20):
    if dashed:
        # PIL dashed border rendered as individual short segments.
        draw.rounded_rectangle((x,y,x+w,y+h), radius=radius, fill=fill)
        for start in range(0, w, 20):
            draw.line((x+start,y,x+min(start+11,w),y), fill=stroke, width=2)
            draw.line((x+start,y+h,x+min(start+11,w),y+h), fill=stroke, width=2)
        for start in range(0,h,20):
            draw.line((x,y+start,x,y+min(start+11,h)), fill=stroke, width=2)
            draw.line((x+w,y+start,x+w,y+min(start+11,h)), fill=stroke, width=2)
    else:
        draw.rounded_rectangle((x,y,x+w,y+h), radius=radius, fill=fill, outline=stroke, width=2)
    svg.append(f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="{radius}" fill="{fill}" stroke="{stroke}" stroke-width="2"'+(' stroke-dasharray="11 9"' if dashed else '')+'/>')
    cell('',f'rounded=1;arcSize=8;fillColor={fill};strokeColor={stroke};strokeWidth=2;dashed={int(dashed)};',x,y,w,h)


def box(x,y,w,h,title,lines,color,proposed=False):
    rect(x,y,w,h,color, '#141c2a' if proposed else BG, proposed)
    text(x+w/2,y+15,title,23,FG,True)
    for i,line in enumerate(lines):
        assert draw.textlength(line,font=font(20)) < w-18, (title,line)
        text(x+w/2,y+53+i*30,line,20)


def line(x1,y1,x2,y2,dashed=False,color=FG,arrow=True):
    length=math.hypot(x2-x1,y2-y1)
    ux,uy=(x2-x1)/length,(y2-y1)/length
    for start in range(0,math.ceil(length),18 if dashed else math.ceil(length)+1):
        end=min(start+(10 if dashed else length),length)
        draw.line((x1+ux*start,y1+uy*start,x1+ux*end,y1+uy*end),fill=color,width=2)
    points=[(x2,y2),(x2-ux*17-uy*9,y2-uy*17+ux*9),(x2-ux*17+uy*9,y2-uy*17-ux*9)]
    if arrow:
        draw.polygon(points,fill=color)
    svg.append(f'<path d="M{x1},{y1} L{x2},{y2}" fill="none" stroke="{color}" stroke-width="2"'+(' stroke-dasharray="10 8"' if dashed else '')+'/>')
    if arrow:
        svg.append('<polygon points="'+' '.join(f'{x},{y}' for x,y in points)+f'" fill="{color}"/>')
    g=cell('',f'endArrow={"block" if arrow else "none"};endFill=1;strokeColor={color};strokeWidth=2;dashed={int(dashed)};',0,0,0,0,True)
    g.set('relative','1')
    ET.SubElement(g,'mxPoint',x=str(x1),y=str(y1),**{'as':'sourcePoint'})
    ET.SubElement(g,'mxPoint',x=str(x2),y=str(y2),**{'as':'targetPoint'})


def layer(y,h,title,color,fill):
    rect(360,y,1600,h,color,fill)
    text(1160,y+17,title,29,color,True)


def exchange(top,bottom,leftlabel,rightlabel):
    line(1125,top,1125,bottom)
    line(1195,bottom,1195,top,True)
    text(860,(top+bottom)/2-12,leftlabel,20)
    text(1490,(top+bottom)/2-12,rightlabel,20)


text(1200,26,'AIVES – KIẾN TRÚC HỆ THỐNG 3 LỚP',42,FG,True)
text(1200,88,'ASP.NET Core Razor Pages / .NET 10 · Đối chiếu main ngày 20/10/2026',23)
text(1200,124,'Hub và Realtime: hai nhóm trách nhiệm riêng trong Presentation · Đã có trong source',21)

layer(185,420,'1. PRESENTATION LAYER – AIVES.WebRazor',BLUE,'#111e34')
box(390,250,490,150,'Razor Pages', ['Account · Admin · Subjects · Topics','Questions · Rubrics · Exams · MyExams','Interview · Grading · Dashboard · Profile'],BLUE)
box(915,250,490,150,'PageModels',['HTTP / form · binding · ModelState','Gọi BLL qua service + DTO','Gọi ILiveUpdates sau khi lưu thành công'],BLUE)
box(1440,250,490,150,'Page view models / Client JS',['wwwroot/js: realtime.js · interview.js','SignalR client · audio / transcript','Razor Pages · cookie · Google OAuth'],BLUE)
box(540,430,575,145,'Hub · SignalR endpoints',['AivesHub: /hubs/aives · presence / groups','InterviewHub: /hubs/interview · audio / transcript','Source: Realtime/AivesHub.cs · InterviewHub.cs'],BLUE)
box(1250,430,575,145,'Realtime · SignalR services',['ILiveUpdates / LiveUpdates · IHubContext','PresenceTracker · ExamExpiryService','Source: AIVES.WebRazor/Realtime/'],BLUE)
line(1250,498,1115,498,True,BLUE)
text(1182,461,'push',18,BLUE)

box(25,260,285,150,'Người dùng',['Giảng viên · Sinh viên · Admin','Trình duyệt Chrome / Edge'],PURPLE)
line(310,315,360,315)
line(360,350,310,350,True)
text(168,432,'HTTPS · HTML / JSON',18)
text(168,461,'SignalR · audio / sự kiện',18,BLUE)
box(2010,260,365,135,'Google OAuth',['/signin-google','OAuth 2.0'],PURPLE)
line(1960,310,2010,310)
line(2010,345,1960,345,True)

exchange(605,705,'Request: service BLL + DTO','Response: DTO → PageModel')
layer(705,395,'2. BUSINESS LOGIC LAYER – AIVES.BLL',GREEN,'#10231e')
box(390,775,365,140,'Question / Bloom',['QuestionService · BloomLevelService','Validation · taxonomy · lọc · CRUD'],GREEN)
box(780,775,365,140,'RubricService',['Tiêu chí · thang điểm · ma trận','Validation · tổng điểm · CRUD'],GREEN)
box(1170,775,365,140,'CatalogService',['Môn học · chủ đề · tài liệu','Import tài liệu · kiểm tra ràng buộc'],GREEN)
box(1560,775,370,140,'ExamService',['Tạo kỳ thi · lịch / slot','ExamQuestionAllocator'],GREEN)
box(390,940,365,135,'Interview / Speech',['InterviewService · STT / TTS','Recording · Gemini / Whisper / Sherpa'],GREEN)
box(780,940,365,135,'AI / Grading',['Gemini · Ollama · Router · Retry','GradingService · GradingWorker'],GREEN)
box(1170,940,365,135,'Account / Operations',['AccountService · UserAdminService','Dashboard · Audit · Backup / Restore'],GREEN)
box(1560,940,370,135,'Email / Diagnostics',['EmailVerification · Gmail SMTP','SystemCheckService · Import parsers'],GREEN)

box(25,760,285,300,'AIVES.DTO',['Question · Bloom · Rubric','Catalog · Exam · Interview','Account · Grading','Dashboard · Operations','GeneratedQuestion / Rubric','AppRoles · L10n','AppLanguage'],PURPLE)
text(168,1080,'DTO dùng chung Web / BLL / DAL',17)
line(310,900,360,900)
box(2010,760,365,125,'Google Gemini API',['Sinh câu hỏi · rubric · hỏi xoáy','HTTPS / JSON'],PURPLE)
box(2010,920,365,125,'Ollama (tùy chọn)',['Model cục bộ · HTTP / JSON','Bật bằng OLLAMA_ENABLED'],PURPLE)
for y in (800,960):
    line(1960,y,2010,y)
    line(2010,y+30,1960,y+30,True)
box(2010,1100,365,125,'Gmail SMTP',['STARTTLS / port 587','Gửi mã xác minh email'],PURPLE)
line(1960,1070,1990,1070,arrow=False)
line(1990,1070,1990,1140,arrow=False)
line(1990,1140,2010,1140)
line(2010,1180,1980,1180,True,arrow=False)
line(1980,1180,1980,1088,True,arrow=False)
line(1980,1088,1960,1088,True)

exchange(1100,1200,'Request: Repository / AccountStore + DTO','Response: DTO / kết quả')
layer(1200,265,'3. DATA ACCESS LAYER – AIVES.DAL',GOLD,'#241e10')
box(390,1270,490,165,'Repositories / interfaces',['Question · Rubric · BloomLevel · Catalog','Exam · Interview · Grading · Recording','Account · Audit · Settings · Diagnostics','DtoMapping: Entity ↔ DTO'],GOLD)
box(915,1270,490,165,'Identity / DbContext',['IdentityAccountStore · ApplicationDbContext','EF Core · ASP.NET Core Identity','DataProtection keys · AdminRoleSeeder','DevelopmentDataSeeder · migrations'],GOLD)
box(1440,1270,490,165,'Entities / Migrations',['Question · Rubric · Criterion / Level / Matrix','Subject · Topic · Material · BloomLevel','Exam · Candidate / Question · Attempt / Turn','ApplicationUser · EmailVerificationCode'],GOLD)
exchange(1465,1565,'Request: EF Core / SQL','Response: kết quả truy vấn')
box(915,1565,490,110,'SQL Server 2022',['Docker: aives-sql-data · Windows: LocalDB'],PURPLE)
line(30,1575,120,1575)
text(210,1560,'Request',20)
line(30,1620,120,1620,True)
text(210,1605,'Response / sự kiện',20)
text(1200,1700,'Hub và Realtime thuộc Presentation; nghiệp vụ thuộc BLL; DAL là tầng duy nhất truy cập database.',22,FG)
text(1200,1736,'Hub và dịch vụ Realtime hiện cùng ở thư mục Realtime/; sơ đồ tách trách nhiệm, không thay đổi source ứng dụng.',20,BLUE)

svg.append('</svg>')
OUT.mkdir(exist_ok=True)
(OUT/'AIVES-3-Layer-Architecture.svg').write_text('\n'.join(svg),encoding='utf-8')
ET.indent(mx)
ET.ElementTree(mx).write(OUT/'AIVES-3-Layer-Architecture.drawio',encoding='utf-8',xml_declaration=True)
im.save(OUT/'AIVES-3-Layer-Architecture.png')
# Parse both editable/vector outputs before reporting success.
ET.parse(OUT/'AIVES-3-Layer-Architecture.svg')
ET.parse(OUT/'AIVES-3-Layer-Architecture.drawio')
print(f'Generated PNG {W}x{H}, SVG and draw.io in {OUT}')
