FROM maven:3.9.15-eclipse-temurin-21-noble

# The Maven image's entrypoint prepares /root/.m2, which agent cannot write.
ENTRYPOINT []

RUN apt-get update && apt-get install -y --no-install-recommends sudo git acl curl procps \
 && rm -rf /var/lib/apt/lists/*

# The Ubuntu base image ships a user with UID 1000; agent takes that UID.
RUN userdel -r ubuntu 2>/dev/null || true

# agent runs the app; runner runs every command the model asks for. sudo keeps
# only the variables listed here, so runner never sees the agent's environment.
RUN groupadd work \
 && useradd -m -u 1000 -G work -s /bin/bash agent \
 && useradd -m -G work runner \
 && chmod 700 /home/agent \
 && install -d -m 1777 -o agent -g agent /home/agent/logs \
 && printf '%s\n' \
    'Defaults:agent !use_pty' \
    'Defaults:agent env_keep += "JAVA_HOME MAVEN_HOME"' \
    "Defaults:agent secure_path=\"$JAVA_HOME/bin:/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin\"" \
    'agent ALL=(runner) NOPASSWD: /usr/bin/timeout' > /etc/sudoers.d/agent \
 && chmod 440 /etc/sudoers.d/agent

# VS Code Dev Containers copies the container's environment into /etc/environment,
# and sudo on Ubuntu loads that file through pam_env, which would hand all of it
# to runner. Commands must start from sudo's clean environment.
RUN sed -i '/pam_env.so/s/^/# /' /etc/pam.d/sudo

# No safe.directory for /workspace: it belongs to runner, and so does its .git/config,
# where the agent's commands could put hooks or filters. git run as agent would run them
# as agent. Participants use in-workspace git ...; VS Code uses git-as-runner (git.path).

RUN mkdir /workspace \
 && chown runner:work /workspace && chmod 2775 /workspace \
 && setfacl -d -m g::rwx /workspace

COPY workspace-seed /opt/workspace-seed
RUN find /opt/workspace-seed -type f -exec chmod 644 {} +
COPY --chmod=755 seed-workspace /usr/local/bin/seed-workspace
COPY --chmod=755 git-as-runner /usr/local/bin/git-as-runner

# Fetch every dependency and plugin now so the workshop day needs no downloads.
# Only the two pom.xml files go into the image, with a tiny program and test of
# our own (image/warmup): the participant's code stays out, so editing it never
# invalidates this layer (the dev container rebuilds the image on every open).
# Building and testing once also fetches what dependency:go-offline misses
# (Surefire's JUnit provider, plugin prefixes).
COPY --chown=agent:agent image/warmup /tmp/build
COPY --chown=agent:agent app/pom.xml /tmp/build/app/pom.xml
COPY --chown=agent:agent app-tests/pom.xml /tmp/build/app-tests/pom.xml
USER agent
RUN mvn -q -B -f /tmp/build/app/pom.xml compile dependency:build-classpath -Dmdep.outputFile=/dev/null  && mvn -q -B -f /tmp/build/app-tests/pom.xml test -Dsurefire.failIfNoSpecifiedTests=false  && rm -rf /tmp/build/*
USER runner
RUN cp -R /opt/workspace-seed /tmp/seed && cd /tmp/seed \
 && mvn -q -B test -Dmaven.test.failure.ignore=true >/dev/null \
 && rm -rf /tmp/seed

USER agent
WORKDIR /home/agent/app
# run-agent, run-tests, reset-workspace and in-workspace live in app/: commands without ./.
ENV PATH="/home/agent/app:${PATH}"
