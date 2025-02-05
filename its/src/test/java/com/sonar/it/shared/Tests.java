/*
 * SonarSource :: C# :: ITs :: Plugin
 * Copyright (C) 2011-2022 SonarSource SA
 * mailto:info AT sonarsource DOT com
 *
 * This program is free software; you can redistribute it and/or
 * modify it under the terms of the GNU Lesser General Public
 * License as published by the Free Software Foundation; either
 * version 3 of the License, or (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
 * Lesser General Public License for more details.
 *
 * You should have received a copy of the GNU Lesser General Public License
 * along with this program; if not, write to the Free Software Foundation,
 * Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.
 */
package com.sonar.it.shared;

import com.sonar.orchestrator.Orchestrator;
import com.sonar.orchestrator.build.BuildResult;
import com.sonar.orchestrator.build.ScannerForMSBuild;
import com.sonar.orchestrator.container.Edition;
import com.sonar.orchestrator.locator.MavenLocation;
import java.io.File;
import java.io.IOException;
import java.nio.file.Path;
import java.nio.file.Paths;
import javax.annotation.CheckForNull;
import javax.annotation.Nullable;
import org.apache.commons.io.FileUtils;
import org.junit.ClassRule;
import org.junit.rules.TemporaryFolder;
import org.junit.runner.RunWith;
import org.junit.runners.Suite;
import org.junit.runners.Suite.SuiteClasses;

@RunWith(Suite.class)
@SuiteClasses({
  CoverageTest.class,
  EnsureAllTestsRunTest.class,
  ScannerCliTest.class,
  VbMainCodeCsTestCodeTest.class
})

public class Tests {

  @ClassRule
  public static final Orchestrator ORCHESTRATOR = Orchestrator.builderEnv()
    .setSonarVersion(TestUtils.replaceLtsVersion(System.getProperty("sonar.runtimeVersion", "DEV")))
    .addPlugin(TestUtils.getPluginLocation("sonar-csharp-plugin"))
    .addPlugin(TestUtils.getPluginLocation("sonar-vbnet-plugin"))
    // ScannerCliTest: Fixed version for the HTML plugin as we don't want to have failures in case of changes there.
    .addPlugin(MavenLocation.of("org.sonarsource.html", "sonar-html-plugin", "3.2.0.2082"))
    .setEdition(Edition.DEVELOPER)
    .activateLicense()
    .build();

  public static Path projectDir(TemporaryFolder temp, String projectName) throws IOException {
    Path projectDir = Paths.get("projects").resolve(projectName);
    FileUtils.deleteDirectory(new File(temp.getRoot(), projectName));
    File newFolder = temp.newFolder(projectName);
    Path tmpProjectDir = Paths.get(newFolder.getCanonicalPath());
    FileUtils.copyDirectory(projectDir.toFile(), tmpProjectDir.toFile());
    return tmpProjectDir;
  }

  static BuildResult analyzeProject(TemporaryFolder temp, String projectName, @Nullable String profileKey, String... keyValues) throws IOException {
    Path projectDir = Tests.projectDir(temp, projectName);
    ScannerForMSBuild beginStep = TestUtils.createBeginStep(projectName, projectDir)
      .setProfile(profileKey)
      .setProperties(keyValues);
    ORCHESTRATOR.executeBuild(beginStep);
    TestUtils.runMSBuild(ORCHESTRATOR, projectDir, "/t:Restore,Rebuild");
    return ORCHESTRATOR.executeBuild(TestUtils.createEndStep(projectDir));
  }

  @CheckForNull
  static Integer getMeasureAsInt(String componentKey, String metricKey) {
    return TestUtils.getMeasureAsInt(ORCHESTRATOR, componentKey, metricKey);
  }
}
