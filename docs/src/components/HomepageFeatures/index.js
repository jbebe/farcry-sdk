import clsx from 'clsx';
import Heading from '@theme/Heading';
import styles from './styles.module.css';

const FeatureList = [
  {
    title: 'Modding Guide',
    description: (
      <>
        Practical, community-sourced workflow for editing Far Cry 2: file
        formats at a usage level, the "Almost Complete Guide", and a survey
        of existing mods and tooling.
      </>
    ),
  },
  {
    title: 'File Formats',
    description: (
      <>
        Every known FC2 container and asset format — merged from community
        findings and from reverse engineering the engine directly, with each
        claim marked by how it was established.
      </>
    ),
  },
  {
    title: 'Engine Internals',
    description: (
      <>
        Reverse-engineering notes on Dunia.dll and the FarCry2 executables:
        the function-callback registry, command-line parsing, the Lua API
        surface, and more — all traced from disassembly.
      </>
    ),
  },
];

function Feature({title, description}) {
  return (
    <div className={clsx('col col--4')}>
      <div className="text--center padding-horiz--md">
        <Heading as="h3">{title}</Heading>
        <p>{description}</p>
      </div>
    </div>
  );
}

export default function HomepageFeatures() {
  return (
    <section className={styles.features}>
      <div className="container">
        <div className="row">
          {FeatureList.map((props, idx) => (
            <Feature key={idx} {...props} />
          ))}
        </div>
      </div>
    </section>
  );
}
