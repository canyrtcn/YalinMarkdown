namespace YalinMarkdown;

internal sealed record LanguageOption(string Code, string Flag, string NativeName)
{
    public override string ToString() => $"{Flag} {NativeName}";
}

internal sealed record Translation(
    string New, string Open, string Save, string SaveAs, string Undo, string UndoTip,
    string Format, string Font, string Size, string Select, string ChooseSize,
    string Editor, string Preview, string LightTheme, string DarkTheme,
    string Ready, string NewDocument, string FileOpened, string Saved, string SessionRestored,
    string PreviewFailed, string Untitled, string AppName,
    string UnsavedQuestion, string Error, string OpenFailed, string SaveFailed,
    string OpenTitle, string SaveTitle, string SaveAsTitle,
    string SupportedFiles, string MarkdownFiles, string TextFiles,
    string Words, string Characters, string Token,
    string Bold, string Italic, string Strike, string InlineCode,
    string Heading1, string Heading2, string Heading3, string Quote,
    string BulletList, string NumberedList, string CodeBlock, string HorizontalRule,
    string BoldPlaceholder, string ItalicPlaceholder, string StrikePlaceholder,
    string CodePlaceholder, string HeadingPlaceholder, string QuotePlaceholder, string ListPlaceholder,
    string TextPlaceholder);

internal static class Localization
{
    internal static readonly LanguageOption[] Languages =
    [
        new("en", "🇬🇧", "English"),
        new("tr", "🇹🇷", "Türkçe"),
        new("de", "🇩🇪", "Deutsch"),
        new("fr", "🇫🇷", "Français"),
        new("es", "🇪🇸", "Español"),
        new("it", "🇮🇹", "Italiano"),
        new("zh", "🇨🇳", "中文"),
        new("ja", "🇯🇵", "日本語"),
        new("ko", "🇰🇷", "한국어"),
        new("ru", "🇷🇺", "Русский")
    ];

    internal static Translation Get(string code) => code switch
    {
        "tr" => new("Yeni", "Aç", "Kaydet", "Farklı Kaydet", "Geri Al", "Geri al (Ctrl+Z)", "Biçim", "Yazı tipi", "Boyut", "Seç...", "Boyut...", "Editör", "Önizleme", "Açık Tema", "Koyu Tema", "Hazır", "Yeni belge", "Dosya açıldı", "Kaydedildi", "Önceki oturum geri yüklendi", "Önizleme geçici olarak oluşturulamadı", "Adsız.md", "Yalın Markdown", "{0} dosyasında kaydedilmemiş değişiklikler var. Kaydetmek ister misiniz?", "Hata", "Dosya açılamadı:\n{0}", "Dosya kaydedilemedi:\n{0}", "Dosya aç", "Kaydet", "Farklı kaydet", "Desteklenen dosyalar", "Markdown dosyaları", "Metin dosyaları", "kelime", "karakter", "token", "Kalın", "İtalik", "Üstü çizili", "Satır içi kod", "Başlık 1", "Başlık 2", "Başlık 3", "Alıntı", "Madde listesi", "Numaralı liste", "Kod bloğu", "Yatay çizgi", "kalın metin", "italik metin", "üstü çizili metin", "kod", "Başlık", "Alıntı", "Liste öğesi", "metin"),
        "de" => new("Neu", "Öffnen", "Speichern", "Speichern unter", "Rückgängig", "Rückgängig (Strg+Z)", "Format", "Schriftart", "Größe", "Auswählen...", "Größe...", "Editor", "Vorschau", "Helles Design", "Dunkles Design", "Bereit", "Neues Dokument", "Datei geöffnet", "Gespeichert", "Vorherige Sitzung wiederhergestellt", "Vorschau konnte vorübergehend nicht erstellt werden", "Unbenannt.md", "Yalın Markdown", "{0} enthält nicht gespeicherte Änderungen. Speichern?", "Fehler", "Datei konnte nicht geöffnet werden:\n{0}", "Datei konnte nicht gespeichert werden:\n{0}", "Datei öffnen", "Speichern", "Speichern unter", "Unterstützte Dateien", "Markdown-Dateien", "Textdateien", "Wörter", "Zeichen", "Token", "Fett", "Kursiv", "Durchgestrichen", "Inline-Code", "Überschrift 1", "Überschrift 2", "Überschrift 3", "Zitat", "Aufzählung", "Nummerierte Liste", "Codeblock", "Horizontale Linie", "fetter Text", "kursiver Text", "durchgestrichener Text", "Code", "Überschrift", "Zitat", "Listenelement", "Text"),
        "fr" => new("Nouveau", "Ouvrir", "Enregistrer", "Enregistrer sous", "Annuler", "Annuler (Ctrl+Z)", "Format", "Police", "Taille", "Choisir...", "Taille...", "Éditeur", "Aperçu", "Thème clair", "Thème sombre", "Prêt", "Nouveau document", "Fichier ouvert", "Enregistré", "Session précédente restaurée", "L’aperçu n’a pas pu être généré temporairement", "Sans titre.md", "Yalın Markdown", "{0} contient des modifications non enregistrées. Enregistrer ?", "Erreur", "Impossible d’ouvrir le fichier :\n{0}", "Impossible d’enregistrer le fichier :\n{0}", "Ouvrir un fichier", "Enregistrer", "Enregistrer sous", "Fichiers pris en charge", "Fichiers Markdown", "Fichiers texte", "mots", "caractères", "jetons", "Gras", "Italique", "Barré", "Code en ligne", "Titre 1", "Titre 2", "Titre 3", "Citation", "Liste à puces", "Liste numérotée", "Bloc de code", "Ligne horizontale", "texte en gras", "texte en italique", "texte barré", "code", "Titre", "Citation", "Élément de liste", "texte"),
        "es" => new("Nuevo", "Abrir", "Guardar", "Guardar como", "Deshacer", "Deshacer (Ctrl+Z)", "Formato", "Fuente", "Tamaño", "Elegir...", "Tamaño...", "Editor", "Vista previa", "Tema claro", "Tema oscuro", "Listo", "Documento nuevo", "Archivo abierto", "Guardado", "Sesión anterior restaurada", "No se pudo generar temporalmente la vista previa", "Sin título.md", "Yalın Markdown", "{0} tiene cambios sin guardar. ¿Guardar?", "Error", "No se pudo abrir el archivo:\n{0}", "No se pudo guardar el archivo:\n{0}", "Abrir archivo", "Guardar", "Guardar como", "Archivos compatibles", "Archivos Markdown", "Archivos de texto", "palabras", "caracteres", "tokens", "Negrita", "Cursiva", "Tachado", "Código en línea", "Título 1", "Título 2", "Título 3", "Cita", "Lista con viñetas", "Lista numerada", "Bloque de código", "Línea horizontal", "texto en negrita", "texto en cursiva", "texto tachado", "código", "Título", "Cita", "Elemento de lista", "texto"),
        "it" => new("Nuovo", "Apri", "Salva", "Salva con nome", "Annulla", "Annulla (Ctrl+Z)", "Formato", "Carattere", "Dimensione", "Scegli...", "Dimensione...", "Editor", "Anteprima", "Tema chiaro", "Tema scuro", "Pronto", "Nuovo documento", "File aperto", "Salvato", "Sessione precedente ripristinata", "Impossibile generare temporaneamente l’anteprima", "Senza titolo.md", "Yalın Markdown", "{0} contiene modifiche non salvate. Salvare?", "Errore", "Impossibile aprire il file:\n{0}", "Impossibile salvare il file:\n{0}", "Apri file", "Salva", "Salva con nome", "File supportati", "File Markdown", "File di testo", "parole", "caratteri", "token", "Grassetto", "Corsivo", "Barrato", "Codice in linea", "Titolo 1", "Titolo 2", "Titolo 3", "Citazione", "Elenco puntato", "Elenco numerato", "Blocco di codice", "Linea orizzontale", "testo in grassetto", "testo in corsivo", "testo barrato", "codice", "Titolo", "Citazione", "Elemento elenco", "testo"),
        "zh" => new("新建", "打开", "保存", "另存为", "撤销", "撤销 (Ctrl+Z)", "格式", "字体", "字号", "选择...", "字号...", "编辑器", "预览", "浅色主题", "深色主题", "就绪", "新文档", "文件已打开", "已保存", "已恢复上次会话", "暂时无法生成预览", "未命名.md", "Yalın Markdown", "{0} 有未保存的更改。是否保存？", "错误", "无法打开文件：\n{0}", "无法保存文件：\n{0}", "打开文件", "保存", "另存为", "支持的文件", "Markdown 文件", "文本文件", "字词", "字符", "令牌", "粗体", "斜体", "删除线", "行内代码", "标题 1", "标题 2", "标题 3", "引用", "项目符号列表", "编号列表", "代码块", "水平线", "粗体文本", "斜体文本", "删除线文本", "代码", "标题", "引用", "列表项", "文本"),
        "ja" => new("新規", "開く", "保存", "名前を付けて保存", "元に戻す", "元に戻す (Ctrl+Z)", "書式", "フォント", "サイズ", "選択...", "サイズ...", "エディター", "プレビュー", "ライトテーマ", "ダークテーマ", "準備完了", "新しい文書", "ファイルを開きました", "保存しました", "前回のセッションを復元しました", "一時的にプレビューを生成できませんでした", "無題.md", "Yalın Markdown", "{0} に未保存の変更があります。保存しますか？", "エラー", "ファイルを開けませんでした:\n{0}", "ファイルを保存できませんでした:\n{0}", "ファイルを開く", "保存", "名前を付けて保存", "対応ファイル", "Markdown ファイル", "テキストファイル", "単語", "文字", "トークン", "太字", "斜体", "取り消し線", "インラインコード", "見出し 1", "見出し 2", "見出し 3", "引用", "箇条書き", "番号付きリスト", "コードブロック", "水平線", "太字テキスト", "斜体テキスト", "取り消し線テキスト", "コード", "見出し", "引用", "リスト項目", "テキスト"),
        "ko" => new("새로 만들기", "열기", "저장", "다른 이름으로 저장", "실행 취소", "실행 취소 (Ctrl+Z)", "서식", "글꼴", "크기", "선택...", "크기...", "편집기", "미리 보기", "밝은 테마", "어두운 테마", "준비됨", "새 문서", "파일을 열었습니다", "저장됨", "이전 세션을 복원했습니다", "미리 보기를 일시적으로 생성할 수 없습니다", "제목 없음.md", "Yalın Markdown", "{0}에 저장하지 않은 변경 사항이 있습니다. 저장할까요?", "오류", "파일을 열 수 없습니다:\n{0}", "파일을 저장할 수 없습니다:\n{0}", "파일 열기", "저장", "다른 이름으로 저장", "지원되는 파일", "Markdown 파일", "텍스트 파일", "단어", "문자", "토큰", "굵게", "기울임꼴", "취소선", "인라인 코드", "제목 1", "제목 2", "제목 3", "인용", "글머리 기호 목록", "번호 매기기 목록", "코드 블록", "가로줄", "굵은 텍스트", "기울임꼴 텍스트", "취소선 텍스트", "코드", "제목", "인용", "목록 항목", "텍스트"),
        "ru" => new("Создать", "Открыть", "Сохранить", "Сохранить как", "Отменить", "Отменить (Ctrl+Z)", "Формат", "Шрифт", "Размер", "Выбрать...", "Размер...", "Редактор", "Предпросмотр", "Светлая тема", "Тёмная тема", "Готово", "Новый документ", "Файл открыт", "Сохранено", "Предыдущий сеанс восстановлен", "Не удалось временно создать предпросмотр", "Без имени.md", "Yalın Markdown", "В файле {0} есть несохранённые изменения. Сохранить?", "Ошибка", "Не удалось открыть файл:\n{0}", "Не удалось сохранить файл:\n{0}", "Открыть файл", "Сохранить", "Сохранить как", "Поддерживаемые файлы", "Файлы Markdown", "Текстовые файлы", "слов", "символов", "токенов", "Жирный", "Курсив", "Зачёркнутый", "Встроенный код", "Заголовок 1", "Заголовок 2", "Заголовок 3", "Цитата", "Маркированный список", "Нумерованный список", "Блок кода", "Горизонтальная линия", "жирный текст", "курсивный текст", "зачёркнутый текст", "код", "Заголовок", "Цитата", "Элемент списка", "текст"),
        _ => new("New", "Open", "Save", "Save As", "Undo", "Undo (Ctrl+Z)", "Format", "Font", "Size", "Select...", "Size...", "Editor", "Preview", "Light Theme", "Dark Theme", "Ready", "New document", "File opened", "Saved", "Previous session restored", "Preview could not be generated temporarily", "Untitled.md", "Yalın Markdown", "{0} has unsaved changes. Save them?", "Error", "Could not open file:\n{0}", "Could not save file:\n{0}", "Open file", "Save", "Save As", "Supported files", "Markdown files", "Text files", "words", "characters", "tokens", "Bold", "Italic", "Strikethrough", "Inline code", "Heading 1", "Heading 2", "Heading 3", "Quote", "Bullet list", "Numbered list", "Code block", "Horizontal rule", "bold text", "italic text", "strikethrough text", "code", "Heading", "Quote", "List item", "text")
    };
}
