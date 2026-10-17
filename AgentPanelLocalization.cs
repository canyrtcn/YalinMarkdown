namespace YalinMarkdown;

// Labels are kept independent of the core editor translations so adding Agent Tools
// does not change the saved language codes or the Translation record contract.
internal sealed record AgentPanelTranslation(
    string Language, string Tools, string ChooseWorkspace, string AnalyzeActive,
    string AnalyzeWorkspace, string Overview, string Findings, string Sections,
    string Files, string MergedContext, string SkillMatch, string Simulate,
    string ExampleTask, string NoWorkspace, string ChooseWorkspaceFirst,
    string AnalyzingDocument, string AnalyzingWorkspace, string AnalyzingSkills,
    string NoSkillMatch);

internal static class AgentPanelLocalization
{
    internal static AgentPanelTranslation Get(string language) => language switch
    {
        "tr" => new("Dil Değiştir", "Agent Araçları", "Çalışma Alanı Seç", "Aktif Dosyayı Analiz Et", "Alanı Analiz Et",
            "Genel Bakış", "Bulgular", "Bölümler", "Dosyalar", "Birleşik Bağlam", "Skill Eşleştirme", "Skill Eşleşmesini Dene",
            "Hangi skill'in tetiklenebileceğini tahmin etmek için örnek görev girin.", "Çalışma alanı seçilmedi",
            "Önce çalışma alanı klasörü seçin.", "Belge analiz ediliyor...", "Çalışma alanı analiz ediliyor...",
            "Skill açıklamaları analiz ediliyor...", "Olası skill eşleşmesi bulunamadı"),
        "de" => new("Sprache ändern", "Agent-Werkzeuge", "Arbeitsbereich wählen", "Aktive Datei prüfen", "Arbeitsbereich prüfen",
            "Übersicht", "Befunde", "Abschnitte", "Dateien", "Kombinierter Kontext", "Skill-Abgleich", "Skill-Auslösung prüfen",
            "Beispielaufgabe eingeben, um mögliche Skills abzuschätzen.", "Kein Arbeitsbereich gewählt",
            "Bitte zuerst einen Arbeitsbereich wählen.", "Dokument wird geprüft...", "Arbeitsbereich wird geprüft...",
            "Skill-Beschreibungen werden geprüft...", "Kein passender Skill gefunden"),
        "fr" => new("Changer de langue", "Outils d’agent", "Choisir un dossier", "Analyser l’actif", "Analyser le dossier",
            "Aperçu", "Résultats", "Sections", "Fichiers", "Contexte fusionné", "Compétences", "Simuler le déclenchement",
            "Saisissez une tâche type pour estimer les compétences mobilisées.", "Aucun dossier choisi",
            "Choisissez d’abord un dossier.", "Analyse du document...", "Analyse du dossier...",
            "Analyse des compétences...", "Aucune compétence probable"),
        "es" => new("Cambiar idioma", "Herramientas de agente", "Elegir carpeta", "Analizar archivo", "Analizar carpeta",
            "Resumen", "Hallazgos", "Secciones", "Archivos", "Contexto combinado", "Coincidencia de skills", "Simular activación",
            "Introduce una tarea de ejemplo para estimar qué skills se activarían.", "No hay carpeta seleccionada",
            "Selecciona primero una carpeta.", "Analizando documento...", "Analizando carpeta...",
            "Analizando skills...", "No se encontraron skills probables"),
        "it" => new("Cambia lingua", "Strumenti agent", "Scegli cartella", "Analizza file", "Analizza cartella",
            "Panoramica", "Risultati", "Sezioni", "File", "Contesto unificato", "Corrispondenza skill", "Simula attivazione",
            "Inserisci un'attività di esempio per stimare gli skill attivabili.", "Nessuna cartella selezionata",
            "Seleziona prima una cartella.", "Analisi del documento...", "Analisi della cartella...",
            "Analisi degli skill...", "Nessuno skill probabile"),
        "zh" => new("切换语言", "代理工具", "选择工作区", "分析当前文件", "分析工作区",
            "概览", "检查结果", "章节", "文件", "合并上下文", "技能匹配", "模拟技能触发",
            "输入示例任务以估计可能触发的技能。", "未选择工作区",
            "请先选择工作区。", "正在分析文档…", "正在分析工作区…",
            "正在分析技能描述…", "未找到可能匹配的技能"),
        "ja" => new("言語を変更", "エージェントツール", "作業フォルダーを選択", "現在のファイルを分析", "作業領域を分析",
            "概要", "検出結果", "セクション", "ファイル", "統合コンテキスト", "スキル照合", "スキルの発火を推定",
            "スキル照合用のタスク例を入力してください。", "作業領域が未選択です",
            "最初に作業領域を選択してください。", "文書を分析中…", "作業領域を分析中…",
            "スキル情報を分析中…", "一致するスキルがありません"),
        "ko" => new("언어 변경", "에이전트 도구", "작업 폴더 선택", "현재 파일 분석", "작업 폴더 분석",
            "개요", "진단 결과", "섹션", "파일", "병합 컨텍스트", "스킬 매칭", "스킬 실행 예측",
            "스킬 매칭을 예측할 예시 작업을 입력하세요.", "작업 폴더가 선택되지 않음",
            "먼저 작업 폴더를 선택하세요.", "문서 분석 중...", "작업 폴더 분석 중...",
            "스킬 설명 분석 중...", "일치하는 스킬 없음"),
        "ru" => new("Сменить язык", "Инструменты агента", "Выбрать папку", "Анализ файла", "Анализ папки",
            "Обзор", "Результаты", "Разделы", "Файлы", "Общий контекст", "Подбор навыков", "Проверить навык",
            "Введите пример задачи для оценки подходящих навыков.", "Папка не выбрана",
            "Сначала выберите рабочую папку.", "Анализ документа...", "Анализ папки...",
            "Анализ описаний навыков...", "Подходящие навыки не найдены"),
        _ => new("Change Language", "Agent Tools", "Choose Workspace", "Analyze Active", "Analyze Workspace",
            "Overview", "Findings", "Sections", "Files", "Merged Context", "Skill Match", "Simulate Skill Trigger",
            "Enter an example task to estimate which skills may trigger.", "No workspace selected",
            "Choose a workspace folder first.", "Analyzing document...", "Analyzing workspace...",
            "Analyzing skill descriptions...", "No likely skill match")
    };
}
