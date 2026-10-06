# AIVES – Viva/Oral Examination Management & Scheduling Module

## 1. Background

Viva/oral examinations are currently used in many educational and professional settings, including:

* Final project and thesis defenses
* End-of-semester oral examinations
* Competency assessments
* Technical interviews
* Practical knowledge evaluations

However, traditional viva examinations create several challenges for instructors and institutions:

* They require a significant amount of instructor time because each student must be interviewed individually.
* It is difficult to standardize questions and difficulty levels across different examination rooms or examiners.
* The examination process becomes difficult to scale when the number of students is large.
* Different examiners may ask substantially different questions, resulting in inconsistent assessment.
* It can be difficult to objectively review how a final score was determined.
* When a student appeals a grade, there may be insufficient evidence to reconstruct the examination.
* There is often no detailed record of which questions were asked, how the student answered, which follow-up questions were used, or how individual points were awarded.

## 2. AIVES Overview

AIVES is an AI-assisted viva examination platform designed to support instructors throughout the oral examination process.

The system should help instructors:

1. Generate and manage examination questions.
2. Create and schedule viva examination sessions.
3. Assign students to examination sessions.
4. Automatically select questions for each student.
5. Use AI to conduct the viva by asking primary questions and adaptive follow-up questions based on the student's answers.
6. Support grading based on predefined rubrics.
7. Record the complete examination process.
8. Store transcripts, questions, answers, follow-up questions, scores, and grading evidence.
9. Provide an auditable examination history in case a score needs to be reviewed or appealed.

---

# 3. Module: Examination Management & Scheduling

## 3.1 Objective

The **Examination Management & Scheduling Module** allows instructors to create, configure, schedule, and manage viva examination sessions.

The module must support the entire lifecycle of an examination session, from initial creation and student assignment through question configuration and scheduling.

---

# 4. Core Requirements

## 4.1 Create a Viva Examination Session

Instructors must be able to create a new viva examination session.

When creating a session, the instructor should provide at least:

* Examination/session name
* Course/subject
* Academic term or semester
* Examination type
* Examination date
* Start time
* End time
* List of participating students
* Duration allocated to each student
* Number of primary questions per student
* Maximum number of follow-up/deep-dive questions per student
* Question selection strategy
* Grading rubric
* Examination instructions or notes

Example:

```text
Session:
    Name: Final Viva Examination – Software Engineering
    Course: Software Engineering
    Semester: Fall 2026
    Date: 2026-12-15
    Start Time: 08:00
    End Time: 12:00

Per Student:
    Duration: 20 minutes
    Primary Questions: 5
    Maximum Follow-up Questions: 3
```

---

# 5. Student Management

The instructor must be able to add or remove students from an examination session.

Students may be selected from:

* The course's existing student list
* A manually uploaded student list
* A university/student management system, if an integration is available
* Manual student entry

Each student should have at least:

* Student ID
* Full name
* Email or institutional account
* Course/class information
* Examination status

Possible examination statuses include:

```text
Not Scheduled
Scheduled
Waiting
In Progress
Completed
Absent
Cancelled
Requires Review
```

The instructor should be able to view the examination status of every student from a single dashboard.

---

# 6. Examination Scheduling

The system must automatically generate an examination schedule based on:

* Examination start time
* Examination end time
* Duration per student
* Number of participating students
* Optional break periods
* Optional buffer time between students
* Examiner availability

For example:

```text
Examination:
08:00 – 12:00

Student 01:
08:00 – 08:20

Student 02:
08:20 – 08:40

Student 03:
08:40 – 09:00

Break:
09:00 – 09:15

Student 04:
09:15 – 09:35
```

The system should prevent scheduling conflicts and should notify the instructor when the configured number of students cannot fit within the selected examination time window.

The instructor should also be able to manually adjust individual student time slots.

---

# 7. Question Configuration

The instructor must be able to configure how questions are selected and presented during the examination.

The system should support two main question categories:

### 7.1 Primary Questions

Primary questions are the main questions used to assess the student's knowledge and competencies.

The instructor should be able to specify:

* Number of primary questions
* Question difficulty
* Question categories/topics
* Learning outcomes or competencies
* Required or optional question pools
* Time allocation per question

Example:

```text
Primary Questions:
    Number: 5

Question Categories:
    Architecture: 2
    Database: 1
    API Design: 1
    Security: 1
```

### 7.2 Follow-up / Deep-Dive Questions

Follow-up questions are generated or selected based on the student's previous answer.

They should be used to:

* Clarify unclear answers
* Test deeper understanding
* Verify whether the student actually understands the concept
* Challenge assumptions made by the student
* Identify misconceptions
* Explore technical reasoning
* Distinguish memorized answers from genuine understanding

The instructor must be able to configure the maximum number of follow-up questions per student.

Example:

```text
Primary Questions: 5
Maximum Follow-up Questions: 3

Maximum Total Questions:
8
```

The system must not exceed the configured maximum unless explicitly authorized by the instructor.

---

# 8. Randomized Question Selection

To reduce question repetition between students, AIVES should automatically select questions from the configured question bank.

The system should support randomized question selection.

For each student, the system should consider:

* Previously used questions
* Questions assigned to students who recently completed the examination
* Question difficulty
* Question category
* Learning objective
* Question usage frequency
* Examination configuration
* Student-specific constraints

The goal is to ensure that students taking the examination consecutively do not receive identical or highly similar question sets.

Example:

```text
Student A:
Q12 → Q31 → Q07 → Q44 → Q19

Student B:
Q25 → Q03 → Q41 → Q18 → Q36

Student C:
Q08 → Q29 → Q14 → Q47 → Q22
```

The system should avoid situations such as:

```text
Student A:
Q12, Q31, Q07, Q44, Q19

Student B:
Q12, Q31, Q07, Q44, Q19
```

unless the instructor explicitly allows question reuse.

---

# 9. Adaptive Question Selection

In addition to simple randomization, AIVES should support adaptive question selection.

The system may use the student's previous answers to determine appropriate follow-up questions.

For example:

```text
Primary Question:
"Explain the difference between REST and GraphQL."

Student Answer:
"REST uses endpoints while GraphQL uses a single endpoint..."

AI Evaluation:
The student demonstrates basic understanding but does not explain
the implications for API performance and data fetching.

Follow-up Question:
"What are the potential performance advantages and disadvantages
of GraphQL compared with REST?"
```

The AI should select or generate an appropriate follow-up question based on:

* The student's answer
* The expected answer
* The learning objective
* The question's difficulty
* Detected misconceptions
* Missing concepts
* The examination rubric
* The maximum follow-up question limit

The adaptive system must remain within the instructor's configured examination constraints.

---

# 10. Question Duplication Prevention

The system should track question usage across the current examination session.

For every question, the system should maintain information such as:

```text
Question ID
Question Category
Difficulty
Learning Objective
Times Used
Last Used At
Students Who Received It
```

When selecting a question for a new student, the system should prioritize questions that have:

* Not been used recently
* Been used less frequently
* Appropriate difficulty
* Appropriate topic coverage
* Not already been assigned to the current student

The system should also be able to detect semantically similar questions, not only questions with identical IDs.

For example:

```text
Question A:
"Explain how database indexing improves query performance."

Question B:
"Why does an index make database queries faster?"
```

Although these are technically different questions, the system should recognize that they assess substantially the same concept and avoid assigning them to consecutive students when possible.

---

# 11. Examination Flow

A typical examination should follow this flow:

```text
1. Instructor creates examination session
        ↓
2. Instructor selects course and students
        ↓
3. Instructor configures examination duration
        ↓
4. Instructor configures primary/follow-up question limits
        ↓
5. Instructor selects question pool and rubric
        ↓
6. System generates examination schedule
        ↓
7. System assigns randomized question sets
        ↓
8. Student enters examination
        ↓
9. AI asks primary question
        ↓
10. Student provides answer
        ↓
11. AI analyzes the answer
        ↓
12. AI determines whether follow-up is necessary
        ↓
13. AI asks adaptive follow-up question if required
        ↓
14. Process repeats until question/time limits are reached
        ↓
15. Examination ends
        ↓
16. Transcript and examination evidence are saved
        ↓
17. AI/instructor grading is performed
        ↓
18. Final score is recorded
```

---

# 12. Examination Audit Trail

AIVES must preserve an objective record of the examination.

The system should record:

* Examination session
* Student
* Examiner/instructor
* Examination start time
* Examination end time
* Questions asked
* Question type
* Question ID
* Question generation/selection method
* Student answers
* Follow-up questions
* AI reasoning/evaluation metadata where appropriate
* Rubric criteria evaluated
* Score awarded for each criterion/question
* Final score
* Manual score adjustments
* Instructor comments
* Timestamps for each event

Example:

```text
08:02:15
Question Q102 asked

08:03:41
Student answer received

08:03:52
AI detected incomplete explanation

08:03:55
Follow-up question F204 generated

08:05:10
Student answer received

08:05:30
Rubric:
    Conceptual Understanding: 4/5
    Technical Accuracy: 3/5
    Reasoning: 4/5
```

This information should remain available for later review.

---

# 13. Instructor Dashboard

The instructor should have a dashboard for managing the examination session.

The dashboard should display:

```text
Session Name
Course
Date
Number of Students
Completed
In Progress
Waiting
Absent
Requires Review
```

Example:

```text
Final Viva – Software Engineering

Students:       80
Completed:      42
In Progress:     1
Waiting:         36
Absent:          1

Average Score: 7.8/10
```

The instructor should be able to:

* Start/pause/end an examination session
* View the current examination
* View student status
* Modify schedules
* Review question assignments
* Review transcripts
* Review scores
* Override AI-generated questions
* Manually intervene during an examination
* Mark an examination for review
* Export examination records

---

# 14. Examination Configuration Rules

The system should validate examination settings before allowing the session to start.

Examples:

* Number of primary questions must be greater than zero.
* Maximum follow-up questions cannot be negative.
* Student duration must be greater than zero.
* The number of students must fit within the examination schedule unless the instructor explicitly overrides the warning.
* The question bank must contain enough suitable questions.
* Each question should belong to at least one valid category or learning objective.
* A grading rubric must be configured before grading is enabled.

If configuration is invalid, the system should provide a clear error message explaining what needs to be fixed.

---

# 15. Functional Requirements Summary

The module must support the following functionality:

### Examination Management

* Create examination sessions
* Edit examination sessions
* Duplicate examination configurations
* Delete/cancel examination sessions
* Start and end examination sessions
* Configure examination rules

### Student Management

* Add students
* Remove students
* Import student lists
* Track examination status
* Assign students to time slots

### Scheduling

* Automatically generate schedules
* Configure student examination duration
* Configure breaks
* Detect scheduling conflicts
* Manually adjust schedules

### Question Management

* Configure question pools
* Configure primary question count
* Configure maximum follow-up question count
* Randomize questions
* Prevent excessive question repetition
* Detect similar questions
* Support adaptive follow-up questions

### Examination Execution

* AI asks questions
* Student provides answers
* AI analyzes answers
* AI generates/selects follow-up questions
* Enforce question limits
* Enforce examination time limits

### Assessment & Evidence

* Store complete transcript
* Store questions and answers
* Store timestamps
* Store per-question scores
* Store rubric-based scores
* Store instructor adjustments
* Preserve examination history for appeals and auditing

---

# 16. Non-Functional Requirements

The system should also consider the following:

### Scalability

The system must support a large number of students and concurrent examination sessions without significant degradation in performance.

### Consistency

Question selection and grading should follow consistent rules across students and examination rooms.

### Auditability

Every important examination event should be timestamped and traceable.

### Reliability

The system should prevent the loss of examination data if a temporary network or application failure occurs.

### Security

Student information, examination questions, transcripts, and scores must be protected from unauthorized access.

### Fairness

Question selection should maintain comparable difficulty and learning-objective coverage across students while reducing direct question repetition.

### Reproducibility

The system should retain enough information about question selection and examination configuration to reconstruct why a particular question was presented to a student.

---

# 17. Example User Story

### Instructor

> As an instructor, I want to create a viva examination session for my course, select participating students, configure the examination duration, specify the number of primary and follow-up questions, and allow AIVES to automatically schedule students and select suitable questions.

### Student

> As a student, I want to participate in a structured oral examination where AIVES asks questions relevant to my course and follows up based on my answers.

### Institution

> As an educational institution, I want every examination to have a complete and auditable record so that examination results can be reviewed objectively if a student appeals a grade.

---

# 18. Expected Outcome

The Examination Management & Scheduling Module should reduce the administrative workload associated with viva examinations while improving consistency, scalability, and transparency.

AIVES should transform a traditional viva from:

```text
Instructor
    ↓
Manually selects questions
    ↓
Asks student
    ↓
Takes notes
    ↓
Manually grades
    ↓
Stores final score
```

into:

```text
AIVES
    ↓
Creates examination session
    ↓
Schedules students
    ↓
Selects balanced/randomized questions
    ↓
AI conducts adaptive questioning
    ↓
Records transcript and examination events
    ↓
Evaluates according to rubric
    ↓
Instructor reviews/adjusts
    ↓
Complete auditable examination record
```

The key objective is not to replace the instructor's academic authority, but to provide a standardized, scalable, and evidence-based infrastructure for conducting and evaluating viva examinations.
