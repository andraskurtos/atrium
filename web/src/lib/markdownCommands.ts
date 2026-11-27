import { EditorSelection, type Extension } from '@codemirror/state';
import { keymap, type Command } from '@codemirror/view';

function hasStandaloneItalicMarkers(
  text: string,
  pos: number,
  side: 'before' | 'after',
): boolean {
  // Count consecutive '*' immediately adjacent to pos
  let count = 0;
  if (side === 'before') {
    let i = pos - 1;
    while (i >= 0 && text[i] === '*') {
      count += 1;
      i -= 1;
    }
  } else {
    let i = pos;
    while (i < text.length && text[i] === '*') {
      count += 1;
      i += 1;
    }
  }
  // Odd number of '*' (1 or 3) indicates italic is present; 2 means bold only
  return count % 2 === 1;
}

function toggleInlineWrapper(marker: string): Command {
  return (view) => {
    const { state } = view;
    const mLen = marker.length;
    const docText = state.doc.toString();
    const changes = state.changeByRange((range) => {
      const selected = state.sliceDoc(range.from, range.to);

      // Unwrap if surrounding text already has the marker (avoiding stripping '*' from '**' bold)
      const before =
        range.from >= mLen
          ? state.sliceDoc(range.from - mLen, range.from)
          : '';
      const after =
        range.to + mLen <= state.doc.length
          ? state.sliceDoc(range.to, range.to + mLen)
          : '';
      const surroundingMatches =
        marker === '*'
          ? before === '*' &&
            after === '*' &&
            hasStandaloneItalicMarkers(docText, range.from, 'before') &&
            hasStandaloneItalicMarkers(docText, range.to, 'after')
          : before === marker && after === marker;

      if (surroundingMatches) {
        return {
          changes: [
            { from: range.from - mLen, to: range.from, insert: '' },
            { from: range.to, to: range.to + mLen, insert: '' },
          ],
          range: EditorSelection.range(range.from - mLen, range.to - mLen),
        };
      }

      // Unwrap if the selection itself starts and ends with the marker
      const selectionMatches =
        selected.length >= mLen * 2 &&
        selected.startsWith(marker) &&
        selected.endsWith(marker) &&
        (marker !== '*' ||
          (hasStandaloneItalicMarkers(selected, 0, 'after') &&
            hasStandaloneItalicMarkers(selected, selected.length, 'before')));

      if (selectionMatches) {
        const inner = selected.slice(mLen, -mLen);
        return {
          changes: { from: range.from, to: range.to, insert: inner },
          range: EditorSelection.range(range.from, range.from + inner.length),
        };
      }

      const wrapped = `${marker}${selected}${marker}`;
      return {
        changes: { from: range.from, to: range.to, insert: wrapped },
        range: EditorSelection.range(
          range.from + mLen,
          range.from + mLen + selected.length,
        ),
      };
    });

    view.dispatch(changes);
    view.focus();
    return true;
  };
}

export const toggleBold: Command = toggleInlineWrapper('**');

export const toggleItalic: Command = toggleInlineWrapper('*');

export function setHeading(level: 1 | 2 | 3): Command {
  return (view) => {
    const { state } = view;
    const prefix = `${'#'.repeat(level)} `;
    const line = state.doc.lineAt(state.selection.main.head);
    const match = line.text.match(/^(#{1,6})\s+/);

    let from = line.from;
    let to = line.from;
    let insert = prefix;

    if (match) {
      to = line.from + match[0].length;
      insert = match[1].length === level ? '' : prefix;
    }

    const delta = insert.length - (to - from);
    const nextCursor = Math.max(line.from, state.selection.main.head + delta);

    view.dispatch({
      changes: { from, to, insert },
      selection: EditorSelection.cursor(nextCursor),
    });
    view.focus();
    return true;
  };
}

export const toggleBlockquote: Command = (view) => {
  const { state } = view;
  const { from, to } = state.selection.main;
  const startLine = state.doc.lineAt(from).number;
  const endLine = state.doc.lineAt(to).number;

  let allQuoted = true;
  for (let n = startLine; n <= endLine; n += 1) {
    if (!state.doc.line(n).text.startsWith('> ')) {
      allQuoted = false;
      break;
    }
  }

  const changes: Array<{ from: number; to: number; insert: string }> = [];
  let firstLineDelta = 0;
  let totalDelta = 0;

  for (let n = startLine; n <= endLine; n += 1) {
    const line = state.doc.line(n);
    if (allQuoted) {
      changes.push({ from: line.from, to: line.from + 2, insert: '' });
      if (n === startLine) firstLineDelta = -2;
      totalDelta -= 2;
    } else if (!line.text.startsWith('> ')) {
      changes.push({ from: line.from, to: line.from, insert: '> ' });
      if (n === startLine) firstLineDelta = 2;
      totalDelta += 2;
    }
  }

  const nextFrom = Math.max(
    state.doc.line(startLine).from,
    from + firstLineDelta,
  );
  const nextTo = Math.max(nextFrom, to + totalDelta);

  view.dispatch({
    changes,
    selection: EditorSelection.range(nextFrom, nextTo),
  });
  view.focus();
  return true;
};

export const insertLink: Command = (view) => {
  const { state } = view;
  const range = state.selection.main;
  const selected = state.sliceDoc(range.from, range.to);
  const label = selected || 'link text';
  const urlPlaceholder = 'https://';
  const insert = `[${label}](${urlPlaceholder})`;
  const urlStart = range.from + 1 + label.length + 2;

  view.dispatch({
    changes: { from: range.from, to: range.to, insert },
    selection: EditorSelection.range(urlStart, urlStart + urlPlaceholder.length),
  });
  view.focus();
  return true;
};

export const insertImage: Command = (view) => {
  const { state } = view;
  const range = state.selection.main;
  const selected = state.sliceDoc(range.from, range.to);
  const alt = selected || 'image description';
  const urlPlaceholder = 'https://';
  const insert = `![${alt}](${urlPlaceholder})`;
  const urlStart = range.from + 2 + alt.length + 2;

  view.dispatch({
    changes: { from: range.from, to: range.to, insert },
    selection: EditorSelection.range(urlStart, urlStart + urlPlaceholder.length),
  });
  view.focus();
  return true;
};

export const insertHorizontalRule: Command = (view) => {
  const { state } = view;
  const pos = state.selection.main.head;
  const line = state.doc.lineAt(pos);
  const needsLeadingNewline = line.text.trim().length > 0;
  const insert = needsLeadingNewline ? '\n\n---\n\n' : '---\n\n';
  const insertAt = needsLeadingNewline ? line.to : line.from;

  view.dispatch({
    changes: { from: insertAt, to: insertAt, insert },
    selection: EditorSelection.cursor(insertAt + insert.length),
  });
  view.focus();
  return true;
};

export const insertCallout: Command = (view) => {
  const { state } = view;
  const range = state.selection.main;
  const selected = state.sliceDoc(range.from, range.to);
  const body = selected
    ? selected
        .split('\n')
        .map((line) => `> ${line}`)
        .join('\n')
    : '> ';

  const line = state.doc.lineAt(range.from);
  const needsLeadingNewline =
    range.empty && line.text.trim().length > 0;
  const prefix = needsLeadingNewline ? '\n\n' : '';
  const header = '> [!note] Note\n';
  const insert = `${prefix}${header}${body}`;
  const insertFrom = needsLeadingNewline ? line.to : range.from;
  const insertTo = needsLeadingNewline ? line.to : range.to;
  const titleStart = insertFrom + prefix.length + '> [!note] '.length;

  view.dispatch({
    changes: { from: insertFrom, to: insertTo, insert },
    selection: EditorSelection.range(titleStart, titleStart + 'Note'.length),
  });
  view.focus();
  return true;
};

export const markdownFormattingKeymap: Extension = keymap.of([
  { key: 'Mod-b', run: toggleBold },
  { key: 'Mod-i', run: toggleItalic },
  { key: 'Mod-Shift-k', run: insertLink, stopPropagation: true },
]);

