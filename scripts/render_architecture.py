"""Render the reviewed AIVES architecture as native draw.io, SVG and PNG.

Usage: python scripts/render_architecture.py (Windows fonts + Pillow).
Nodes are editable labels/shapes; edges bind to node IDs, including inside layers.
"""
from pathlib import Path
from html import escape
import math
import xml.etree.ElementTree as ET
from PIL import Image, ImageDraw, ImageFont

OUT = Path(__file__).resolve().parents[1] / 'docs'
W, H = 2500, 1860
INK, MUTED = '#233247', '#536379'
BLUE, GREEN, GOLD, PURPLE = '#4476ad', '#237e70', '#9b7835', '#8861ac'
im = Image.new('RGB', (W, H), 'white')
draw = ImageDraw.Draw(im)
svg = [f'<svg xmlns="http://www.w3.org/2000/svg" width="{W}" height="{H}" viewBox="0 0 {W} {H}">', '<rect width="100%" height="100%" fill="white"/>']
mx = ET.Element('mxfile', host='app.diagrams.net')
diagram = ET.SubElement(mx, 'diagram', name='AIVES — 3 Layer Architecture', id='aives-reviewed')
model = ET.SubElement(diagram, 'mxGraphModel', page='1', pageScale='1', pageWidth=str(W), pageHeight=str(H), background='#ffffff', grid='0')
root = ET.SubElement(model, 'root')
ET.SubElement(root, 'mxCell', id='0')
ET.SubElement(root, 'mxCell', id='1', parent='0')
nodes = {}


def font(size, bold=False):
    return ImageFont.truetype('C:/Windows/Fonts/' + ('segoeuib.ttf' if bold else 'segoeui.ttf'), size)


def paint_text(x, y, value, size=22, color=INK, bold=False):
    draw.text((x, y), value, anchor='mt', fill=color, font=font(size, bold))
    svg.append(f'<text x="{x}" y="{y+size}" text-anchor="middle" font-family="Segoe UI,Arial,sans-serif" font-size="{size}" font-weight="{700 if bold else 400}" fill="{color}">{escape(value)}</text>')


def note(id, x, y, w, value, size=21, color=MUTED):
    paint_text(x+w/2, y, value, size, color)
    c = ET.SubElement(root, 'mxCell', id=id, value=value, vertex='1', parent='1', style=f'text;html=0;align=center;verticalAlign=top;fontFamily=Segoe UI;fontSize={size};fontColor={color};')
    ET.SubElement(c, 'mxGeometry', x=str(x), y=str(y), width=str(w), height=str(size+14), **{'as':'geometry'})


def node(id, x, y, w, h, title, lines=(), stroke=BLUE, fill='#f5f8fc', parent='1', container=False, size=21):
    assert id not in nodes
    assert all(draw.textlength(s, font=font(size)) < w-26 for s in lines), (id, lines)
    assert draw.textlength(title, font=font(26 if container else 24, True)) < w-25, title
    nodes[id] = (x,y,w,h)
    draw.rounded_rectangle((x,y,x+w,y+h), radius=20, fill=fill, outline=stroke, width=3 if container else 2)
    svg.append(f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="20" fill="{fill}" stroke="{stroke}" stroke-width="{3 if container else 2}"/>')
    title_size = 26 if container else 24
    paint_text(x+w/2,y+18,title,title_size,stroke if container else INK,True)
    for i,s in enumerate(lines):
        paint_text(x+w/2,y+58+i*30,s,size,MUTED)
    html = f'<div style="font-size:{title_size}px;font-weight:bold;color:{stroke if container else INK};margin-bottom:12px">{escape(title)}</div>'
    html += ''.join(f'<div style="font-size:{size}px;line-height:30px;color:{MUTED}">{escape(s)}</div>' for s in lines)
    style = f'rounded=1;arcSize=8;whiteSpace=wrap;html=1;align=center;verticalAlign=top;spacingTop=16;spacingLeft=10;spacingRight=10;fillColor={fill};strokeColor={stroke};strokeWidth={3 if container else 2};fontFamily=Segoe UI;'
    if container:
        style += 'container=1;collapsible=0;recursiveResize=0;'
    c = ET.SubElement(root,'mxCell',id=id,value=html,style=style,vertex='1',parent=parent)
    px,py = (0,0) if parent == '1' else nodes[parent][:2]
    ET.SubElement(c,'mxGeometry',x=str(x-px),y=str(y-py),width=str(w),height=str(h),**{'as':'geometry'})


def edge(id, source, target, start, end, color=INK, dashed=False, label='', points=()):
    # Fixed waypoints make SVG/PNG and draw.io agree; source/target IDs stay attached.
    path = [start,*points,end]
    for (x1,y1),(x2,y2) in zip(path,path[1:]):
        length=math.hypot(x2-x1,y2-y1)
        ux,uy=(x2-x1)/length,(y2-y1)/length
        step=18 if dashed else max(1,math.ceil(length))
        for n in range(0,math.ceil(length),step):
            z=min(n+(10 if dashed else length),length)
            draw.line((x1+ux*n,y1+uy*n,x1+ux*z,y1+uy*z),fill=color,width=2)
    x1,y1=path[-2]; x2,y2=end
    length=math.hypot(x2-x1,y2-y1); ux,uy=(x2-x1)/length,(y2-y1)/length
    tri=[end,(x2-ux*15-uy*7,y2-uy*15+ux*7),(x2-ux*15+uy*7,y2-uy*15-ux*7)]
    draw.polygon(tri,fill=color)
    svg.append('<polyline points="'+' '.join(f'{x},{y}' for x,y in path)+f'" fill="none" stroke="{color}" stroke-width="2"'+(' stroke-dasharray="10 8"' if dashed else '')+'/>')
    svg.append('<polygon points="'+' '.join(f'{x},{y}' for x,y in tri)+f'" fill="{color}"/>')
    sx,sy,sw,sh=nodes[source]; tx,ty,tw,th=nodes[target]
    style=f'edgeStyle=none;rounded=0;html=0;endArrow=block;endFill=1;strokeWidth=2;strokeColor={color};dashed={int(dashed)};exitX={(start[0]-sx)/sw};exitY={(start[1]-sy)/sh};entryX={(end[0]-tx)/tw};entryY={(end[1]-ty)/th};exitPerimeter=0;entryPerimeter=0;fontFamily=Segoe UI;fontSize=18;'
    c=ET.SubElement(root,'mxCell',id=id,value='',edge='1',parent='1',source=source,target=target,style=style)
    g=ET.SubElement(c,'mxGeometry',relative='1',**{'as':'geometry'})
    if points:
        arr=ET.SubElement(g,'Array',**{'as':'points'})
        for x,y in points:
            ET.SubElement(arr,'mxPoint',x=str(x),y=str(y))
    if label:
        note(id+'-label',min(start[0],end[0])-540,(start[1]+end[1])/2-14,450,label,18,color)


note('title',420,24,1550,'AIVES — KIẾN TRÚC HỆ THỐNG 3 LỚP',36,INK)
note('subtitle',420,82,1550,'Razor Pages / .NET 10 · Review source E:\\PRN213\\AIVES_System · 24/10/2026',22)
node('client',420,145,1550,145,'NGƯỜI DÙNG / TRÌNH DUYỆT',['Giảng viên · Sinh viên · Admin — Chrome / Edge','HTTPS · HTML/JSON · SignalR client · Web Speech STT'],GREEN,'#e7f3e6',container=True)
node('presentation',420,365,1550,265,'PRESENTATION LAYER — AIVES.WebRazor',stroke=BLUE,fill='#eaf1fb',container=True)
node('pages',450,435,475,165,'Razor Pages / PageModels',['Pages/ · binding · ModelState','Gọi BLL bằng service + DTO','View models · wwwroot/js'],parent='presentation')
node('hubs',957,435,475,165,'Hub · SignalR endpoints',['AivesHub: /hubs/aives','InterviewHub: /hubs/interview','Presence / groups · giám sát phiên thi'],parent='presentation')
node('realtime',1465,435,475,165,'Realtime · SignalR services',['ILiveUpdates / LiveUpdates','IHubContext · PresenceTracker','ExamExpiryService'],parent='presentation')

node('business',420,740,1550,420,'BUSINESS LOGIC LAYER — AIVES.BLL',stroke=GREEN,fill='#e9f3ef',container=True)
modules=[
 ('question','Question / Bloom',['QuestionService · taxonomy','BloomLevelService · validation']),
 ('rubric','Rubric service',['RubricService','Tiêu chí · ma trận · tổng điểm']),
 ('catalog','Catalog service',['Môn học · chủ đề · tài liệu','Import · MaterialRetriever']),
 ('exam','Exam service',['ExamService · lịch / ứng viên','ExamQuestionAllocator']),
 ('interview','Interview / Speech',['InterviewService · follow-up','TTS: VieNeu / Gemini / local']),
 ('llm','AI / Grading',['Gemini / Ollama · routers','GradingService · GradingWorker']),
 ('accounts','Account / Operations',['Account · UserAdmin · Dashboard','Audit · Settings · Glossary']),
 ('email','Email / Diagnostics',['OTP · GmailSmtpEmailSender','SystemCheckService · Recording']),
]
for i,(id,title,lines) in enumerate(modules):
    node(id,450+(i%4)*380,815+(i//4)*165,350,140,title,lines,GREEN,'#f7fbf9','business',size=19)

node('data',420,1270,1550,260,'DATA ACCESS LAYER — AIVES.DAL',stroke=GOLD,fill='#f7f0e3',container=True)
node('repos',450,1340,475,160,'Repositories / DtoMapping',['Question · Catalog · Exam · Interview','Grading · Recording · Audit · Settings','DTO ↔ entity mapping'],GOLD,'#fffcf6','data')
node('identity',957,1340,475,160,'Identity / DbContext',['IdentityAccountStore','ApplicationDbContext · EF Core','DataProtection keys · seed / init'],GOLD,'#fffcf6','data')
node('entities',1465,1340,475,160,'Entities / Migrations',['Question · Rubric · Catalog · Exam','Attempt / Turn · Grading · Recording','Identity · 13 migrations'],GOLD,'#fffcf6','data')
node('sql',957,1620,475,110,'SQL Server 2022',['Docker volume / Windows LocalDB'],PURPLE,'#f1ebf8')

node('dto',45,365,285,1165,'AIVES.DTO',[],PURPLE,'#f1ebf8',container=True)
for i,s in enumerate(['Dữ liệu dùng chung','Question / Bloom','Rubric / Catalog','Exam / Interview','Account / Grading','Dashboard / Operations','AppRoles / Localization','Không phải tầng xử lý']):
    note('dto-text-'+str(i),50,690+i*48,275,s,20,PURPLE)
node('oauth',2050,435,400,145,'Google OAuth',['HTTP authentication','/signin-google'],PURPLE,'#f1ebf8')
node('thirdparty',2050,740,400,625,'DỊCH VỤ TÍCH HỢP',stroke=PURPLE,fill='#f7f3fa',container=True)
node('gemini',2070,810,360,110,'Google Gemini API',['AI / grading / TTS · HTTPS'],PURPLE,'#ffffff','thirdparty',size=19)
node('ollama',2070,945,360,110,'Ollama · tùy chọn',['LLM local · HTTP / JSON'],PURPLE,'#ffffff','thirdparty',size=19)
node('smtp',2070,1080,360,110,'Gmail SMTP',['STARTTLS · port 587'],PURPLE,'#ffffff','thirdparty',size=19)
node('vieneu',2070,1215,360,120,'VieNeu-TTS · tùy chọn',['HTTP · /v1/audio/speech','Đọc câu hỏi tiếng Việt'],PURPLE,'#ffffff','thirdparty',size=19)
node('files',2050,1480,400,145,'Recording storage',['FileRecordingStore (BLL)','Encrypted files · volume recordings'],PURPLE,'#f7f3fa',size=19)

# Processing flows. DTO dependencies use a separate colour and dotted style.
edge('client-request','client','presentation',(1160,290),(1160,365))
edge('client-response','presentation','client',(1220,365),(1220,290),dashed=True)
edge('web-bll','presentation','business',(1160,630),(1160,740),label='Service + DTO')
edge('bll-web','business','presentation',(1220,740),(1220,630),dashed=True)
edge('bll-dal','business','data',(1160,1160),(1160,1270),label='Repository / AccountStore')
edge('dal-bll','data','business',(1220,1270),(1220,1160),dashed=True)
edge('dal-sql','data','sql',(1160,1530),(1160,1620),label='EF Core / SQL')
edge('sql-dal','sql','data',(1220,1620),(1220,1530),dashed=True)
for id,y in [('presentation',500),('business',950),('data',1400)]:
    edge('dto-'+id,id,'dto',(420,y),(330,y),PURPLE,True)
edge('oauth-request','presentation','oauth',(1970,490),(2050,490))
edge('oauth-response','oauth','presentation',(2050,535),(1970,535),dashed=True)
edge('api-request','business','thirdparty',(1970,940),(2050,940))
edge('api-response','thirdparty','business',(2050,990),(1970,990),dashed=True)
edge('recording-files','business','files',(1970,1130),(2050,1550),points=((2010,1130),(2010,1550)))
note('recording-protocol',2020,1650,440,'File I/O qua IRecordingStore',19,PURPLE)
note('hub-note',450,650,600,'Hub / Realtime: cùng thư mục Realtime/',20,BLUE)
note('hub-note2',1350,650,590,'WatchExam: giám sát · trả lời bài thi qua HTTP',20,BLUE)
note('legend1',45,1610,285,'Nét liền: request / call',19)
note('legend2',45,1650,285,'Nét đứt đen: response',19)
note('legend3',45,1690,285,'Nét đứt tím: dùng DTO',19,PURPLE)
note('footer1',350,1770,1800,'SignalR push là sự kiện chủ động tới client; không đồng nghĩa với response của một HTTP request.',22)
note('footer2',350,1810,1800,'Presentation → BLL → DAL; DTO dùng chung. File bản ghi được lưu qua adapter riêng ở BLL.',22)

OUT.mkdir(exist_ok=True)
svg.append('</svg>')
stem=OUT/'AIVES-3-Layer-Architecture'
stem.with_suffix('.svg').write_text('\n'.join(svg),encoding='utf-8')
ET.indent(mx)
ET.ElementTree(mx).write(stem.with_suffix('.drawio'),encoding='utf-8',xml_declaration=True)
im.save(stem.with_suffix('.png'))
# Structural verification for editable diagram delivery.
ET.parse(stem.with_suffix('.svg'))
parsed=ET.parse(stem.with_suffix('.drawio'))
cells=parsed.findall('.//mxCell'); ids={c.get('id') for c in cells}
assert len(ids)==len(cells), 'Duplicate draw.io IDs'
for c in cells:
    assert c.get('parent','0') in ids
    if c.get('edge')=='1':
        assert c.get('source') in nodes and c.get('target') in nodes
for c in cells:
    if c.get('vertex')=='1' and c.get('parent') in nodes:
        p=nodes[c.get('parent')]; n=nodes[c.get('id')]
        assert p[0]<=n[0] and p[1]<=n[1] and n[0]+n[2]<=p[0]+p[2] and n[1]+n[3]<=p[1]+p[3]
print(f'OK: {len(nodes)} nodes, {sum(c.get("edge")=="1" for c in cells)} attached edges; PNG {W}x{H}, SVG, draw.io → {OUT}')
