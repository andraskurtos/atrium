import {
  Children,
  createElement,
  isValidElement,
  useEffect,
  useState,
  type ReactNode,
} from 'react';
import type { Components, ExtraProps } from 'react-markdown';
import {
  isInternalAttachmentUrl,
  resolveAttachmentImageUrl,
} from './attachmentsClient.ts';
import {
  CALLOUT_GLYPHS,
  formatCalloutDefaultTitle,
  resolveCalloutTone,
  sanitizeMarkdownUrl,
} from './markdownUtils.ts';

const CALLOUT_PREFIX_RE =
  /^\[!([a-zA-Z0-9_-]+)\][+-]?(?:$|([ \t]+)([\s\S]*)$)/;

type HastNodeProp = ExtraProps['node'];

function getSingleImageProps(
  node: HastNodeProp,
): { src?: string; alt?: string } | null {
  if (!node || node.children.length !== 1) return null;
  const only = node.children[0];
  if (only.type !== 'element' || only.tagName !== 'img') return null;
  const props = only.properties ?? {};
  const src = typeof props.src === 'string' ? props.src : undefined;
  const alt = typeof props.alt === 'string' ? props.alt : undefined;
  return { src, alt };
}

export function SafeReadingImage({
  src,
  alt,
  standalone = false,
}: {
  src?: string;
  alt?: string;
  standalone?: boolean;
}) {
  const safeSrc = src ? sanitizeMarkdownUrl(src, 'image') : null;
  const isInternal = safeSrc ? isInternalAttachmentUrl(safeSrc) : false;
  const [resolvedSrc, setResolvedSrc] = useState<string | null>(
    isInternal ? null : safeSrc,
  );
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    setFailed(false);
    if (!safeSrc) {
      setResolvedSrc(null);
      return;
    }
    if (!isInternal) {
      setResolvedSrc(safeSrc);
      return;
    }

    let cancelled = false;
    void resolveAttachmentImageUrl(safeSrc).then((blobUrl) => {
      if (cancelled) return;
      if (!blobUrl) {
        setFailed(true);
      } else {
        setResolvedSrc(blobUrl);
      }
    });
    return () => {
      cancelled = true;
    };
  }, [isInternal, safeSrc]);

  if (!safeSrc || failed) {
    const fallbackMessage = !safeSrc
      ? alt
        ? `Blocked or invalid image URL (${alt})`
        : 'Blocked or invalid image URL'
      : alt
        ? `Image failed to load: ${alt}`
        : 'Image failed to load';
    const badge = createElement(
      'span',
      { className: 'cm-md-image-fallback' },
      fallbackMessage,
    );
    return standalone
      ? createElement('div', { className: 'cm-md-image-figure' }, badge)
      : badge;
  }

  if (!resolvedSrc) {
    return createElement(
      'span',
      { className: 'meta-text' },
      'Loading image…',
    );
  }

  const imgEl = createElement('img', {
    className: standalone ? 'cm-md-image' : 'cm-md-image cm-md-image-inline',
    src: resolvedSrc,
    alt: alt ?? '',
    loading: 'lazy',
    decoding: 'async',
    referrerPolicy: 'no-referrer',
    onError: () => setFailed(true),
  });

  if (standalone) {
    return createElement(
      'figure',
      { className: 'cm-md-image-figure' },
      imgEl,
      alt && alt.trim().length > 0
        ? createElement('figcaption', { className: 'cm-md-image-caption' }, alt)
        : null,
    );
  }

  return createElement(
    'span',
    { className: 'notes-reading-inline-image' },
    imgEl,
  );
}

function ReadingParagraph({
  node,
  children,
}: {
  node?: HastNodeProp;
  children?: ReactNode;
}) {
  const singleImg = getSingleImageProps(node);
  if (singleImg) {
    return createElement(SafeReadingImage, {
      key: singleImg.src ?? '',
      src: singleImg.src,
      alt: singleImg.alt,
      standalone: true,
    });
  }
  return createElement('p', null, children);
}

export function renderReadingBlockquote(children: ReactNode) {
  const childArray = Children.toArray(children).filter(
    (c) => !(typeof c === 'string' && c.trim() === ''),
  );
  const first = childArray[0];

  if (
    isValidElement<{ children?: ReactNode; node?: HastNodeProp }>(first) &&
    (first.type === 'p' || first.type === ReadingParagraph) &&
    !getSingleImageProps(first.props.node)
  ) {
    const firstChildren = Children.toArray(first.props.children);
    const headerNodes: ReactNode[] = [];
    const restFirstParagraph: ReactNode[] = [];
    let foundBreak = false;
    let stripLeadingNewlineAfterBr = false;

    for (const child of firstChildren) {
      if (!foundBreak) {
        if (isValidElement(child) && child.type === 'br') {
          foundBreak = true;
          stripLeadingNewlineAfterBr = true;
          continue;
        }
        if (typeof child === 'string') {
          const nlIndex = child.indexOf('\n');
          if (nlIndex !== -1) {
            const before = child.slice(0, nlIndex);
            const after = child.slice(nlIndex + 1);
            if (before) headerNodes.push(before);
            if (after) restFirstParagraph.push(after);
            foundBreak = true;
            continue;
          }
        }
        headerNodes.push(child);
      } else {
        if (stripLeadingNewlineAfterBr) {
          stripLeadingNewlineAfterBr = false;
          if (typeof child === 'string' && child.startsWith('\n')) {
            const trimmed = child.slice(1);
            if (trimmed) restFirstParagraph.push(trimmed);
            continue;
          }
        }
        restFirstParagraph.push(child);
      }
    }

    const firstHeaderNode = headerNodes[0];
    if (typeof firstHeaderNode === 'string') {
      const match = CALLOUT_PREFIX_RE.exec(firstHeaderNode);
      const hasValidBoundary =
        match !== null && (Boolean(match[2]) || headerNodes.length === 1);

      if (match && hasValidBoundary) {
        const rawType = match[1];
        const tone = resolveCalloutTone(rawType);
        const firstTitleRemainder = match[3] ?? '';
        const rawTitleNodes: ReactNode[] = [
          ...(firstTitleRemainder ? [firstTitleRemainder] : []),
          ...headerNodes.slice(1),
        ];

        if (
          rawTitleNodes.length > 0 &&
          typeof rawTitleNodes[rawTitleNodes.length - 1] === 'string'
        ) {
          const trimmedEnd = (
            rawTitleNodes[rawTitleNodes.length - 1] as string
          ).replace(/[ \t]+$/, '');
          if (trimmedEnd) {
            rawTitleNodes[rawTitleNodes.length - 1] = trimmedEnd;
          } else {
            rawTitleNodes.pop();
          }
        }

        const hasCustomTitle = rawTitleNodes.some(
          (n) => !(typeof n === 'string' && n.trim() === ''),
        );
        const titleContent: ReactNode = hasCustomTitle
          ? rawTitleNodes
          : formatCalloutDefaultTitle(rawType);

        const bodyNodes: ReactNode[] = [];
        const hasRestFirstParagraph = restFirstParagraph.some(
          (n) => !(typeof n === 'string' && n.trim() === ''),
        );
        if (hasRestFirstParagraph) {
          bodyNodes.push(
            createElement(
              'p',
              { key: 'callout-first-body' },
              ...restFirstParagraph,
            ),
          );
        }
        bodyNodes.push(...childArray.slice(1));

        return createElement(
          'div',
          { className: `notes-callout is-${tone}` },
          createElement(
            'div',
            { className: `notes-callout-header is-${tone}` },
            createElement(
              'span',
              { className: 'cm-md-callout-icon', 'aria-hidden': 'true' },
              CALLOUT_GLYPHS[tone],
            ),
            createElement('span', null, titleContent),
          ),
          bodyNodes.length > 0
            ? createElement('div', { className: 'notes-callout-body' }, bodyNodes)
            : null,
        );
      }
    }
  }

  return createElement('blockquote', null, children);
}

export const readingMarkdownComponents: Components = {
  p: ReadingParagraph,
  blockquote({ children }) {
    return renderReadingBlockquote(children);
  },
  a({ href, children }) {
    const safeHref = href ? sanitizeMarkdownUrl(href, 'link') : null;
    if (!safeHref) {
      return createElement(
        'span',
        {
          className: 'cm-md-link-invalid',
          title: 'Blocked unsafe or invalid URL',
        },
        children,
      );
    }
    return createElement(
      'a',
      {
        href: safeHref,
        target: '_blank',
        rel: 'noopener noreferrer',
        className: 'notes-reading-link',
      },
      children,
    );
  },
  img({ src, alt }) {
    return createElement(SafeReadingImage, {
      key: src ?? '',
      src,
      alt,
      standalone: false,
    });
  },
};
