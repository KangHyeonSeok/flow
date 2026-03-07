# Spec Graph - VSCode Extension

**기능 의존성 그래프(Feature Dependency Graph)** 시각화 VSCode 확장

## 기능

### 🌳 스펙 트리뷰 (Sidebar)
- `docs/specs/*.json` 스펙 파일을 계층 구조(Feature → Condition)로 표시
- 상태별 아이콘 및 색상 표시 (draft / queued / working / needs-review / verified / deprecated / done)
- 상태·태그별 필터링
- 노드 클릭 시 상세 패널 연동

### 📊 그래프 뷰 (Webview + Cytoscape.js)
- Feature 노드: 둥근 사각형, 상태별 색상
- Condition 노드: 원형, 소형, Feature 하위로 연결
- 엣지 종류:
  - **Parent** (실선): 계층 관계
  - **Dependency** (점선, 파란색): 의존 관계
  - **Condition** (가는 실선): Feature → Condition 연결
- 레이아웃 전환: CoSE / Breadthfirst / Circle / Concentric
- 상태 필터, 조건 노드 표시 토글
- 줌/핏/포커스 컨트롤

### 📋 상세 패널 (Sidebar Webview)
- 선택된 노드의 상세 정보 표시
- 상태 배지, 태그, 설명
- Code References: 클릭 시 에디터에서 해당 파일·라인으로 이동
- Conditions 목록 (Feature 선택 시)
- 스펙 JSON 파일 열기 버튼

### 🔗 코드 참조 이동
- `codeRefs`의 `파일경로#L시작-L끝` 형식 지원
- 클릭 시 VSCode 에디터에서 해당 파일의 해당 라인으로 즉시 이동

## 사용법

1. `docs/specs/` 폴더에 스펙 JSON 파일이 있으면 자동 활성화
2. Activity Bar의 **Spec Graph** 아이콘 클릭 → 트리뷰 열기
3. 트리뷰 상단 **그래프 열기** 버튼 → Cytoscape.js 그래프 뷰 열기
4. 노드 클릭 → 상세 패널 + 그래프 포커스 연동

## 빌드

```bash
cd tools/flow-ext
npm install
npm run build
```

## 개발

```bash
npm run watch    # TypeScript 파일 변경 감시
# F5 → Extension Development Host 실행
```

## 스펙 JSON 스키마 (v2)

```jsonc
{
  "schemaVersion": 2,
  "id": "F-010",
  "nodeType": "feature",
  "title": "스펙 그래프 관리",
  "status": "queued",        // draft | queued | working | needs-review | verified | deprecated | done
  "parent": "F-001",
  "dependencies": ["F-002"],
  "conditions": [
    {
      "id": "F-010-C1",
      "nodeType": "condition",
      "description": "Given ... When ... Then ...",
      "status": "verified",
      "codeRefs": ["tools/flow-cli/Services/Foo.cs#L20-L30"],
      "evidence": []
    }
  ],
  "codeRefs": ["tools/flow-cli/Commands/SpecCommand.cs"],
  "tags": ["spec", "graph"]
}
```

## 아키텍처

```
flow-ext/
├── src/
│   ├── extension.ts         # 진입점, 명령어 등록
│   ├── types.ts             # 타입 정의 (Spec, GraphNode, Edge)
│   ├── specLoader.ts        # JSON 파일 로드, 그래프 빌드, Kahn 순환 감지
│   ├── specTreeProvider.ts  # TreeView 데이터 프로바이더
│   ├── graphPanel.ts        # Cytoscape.js Webview 패널
│   └── detailViewProvider.ts# 사이드바 상세 Webview
├── resources/
│   └── icon.svg             # Activity Bar 아이콘
├── package.json             # Extension 매니페스트
└── tsconfig.json
```
