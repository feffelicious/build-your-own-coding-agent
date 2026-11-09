FROM python:3.12.14-slim-bookworm

ENV PIP_DISABLE_PIP_VERSION_CHECK=1 \
    PYTHONDONTWRITEBYTECODE=1

RUN apt-get update && apt-get install -y --no-install-recommends sudo git acl curl procps \
 && rm -rf /var/lib/apt/lists/*

# agent runs the app; runner runs every command the model asks for. sudo keeps
# only the variables listed here, so runner never sees the agent's environment.
RUN groupadd work \
 && useradd -m -u 1000 -G work -s /bin/bash agent \
 && useradd -m -G work runner \
 && chmod 700 /home/agent \
 && install -d -m 1777 -o agent -g agent /home/agent/logs \
 && printf '%s\n' \
    'Defaults:agent !use_pty' \
    'Defaults:agent env_keep += "PYTHONDONTWRITEBYTECODE"' \
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

# Install every package now (system-wide, for agent and runner) so the
# workshop day needs no downloads.
COPY app/requirements.lock /tmp/requirements.lock
RUN pip install --no-cache-dir --no-deps -r /tmp/requirements.lock && rm /tmp/requirements.lock

USER agent
WORKDIR /home/agent/app
# run-agent, run-tests, reset-workspace and in-workspace live in app/: commands without ./.
ENV PATH="/home/agent/app:${PATH}"
