"""Setup configuration for e2e-test tool."""

from setuptools import setup, find_packages

setup(
    name="e2e-test",
    version="0.1.0",
    description="E2E test tool for Flow project",
    packages=find_packages(),
    python_requires=">=3.12",
    install_requires=[
        "requests>=2.31.0",
        "pyyaml>=6.0",
        "pillow>=10.0.0",
        "google-generativeai>=0.3.0",
        "click>=8.1.0",
    ],
    entry_points={
        "console_scripts": [
            "e2e-test=e2e_test.cli:main",
        ],
    },
)
