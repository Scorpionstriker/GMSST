What is the Genric Moduler System Scheduler and what is its purpose: The Generic Modular System Scheduler (GMSS) 
is a software framework designed to manage and schedule tasks or processes in a modular and flexible manner. 
Its primary purpose is to provide a structured approach to task scheduling, allowing for efficient resource allocation,
prioritization of tasks, and optimization of system performance. The GMSS can be used in various applications,
including operating systems, real-time systems, and distributed computing environments,
where it helps ensure that tasks are executed in a timely and organized fashion while adapting to changing workloads and system conditions.

Moreover, the GMSS supports modularity, enabling developers to easily integrate new scheduling algorithms or policies without significant 
changes to the existing system. This flexibility allows for customization based on specific application requirements and performance goals.
By providing a standardized interface for task management, the GMSS facilitates better coordination among different components of a system,
leading to improved overall efficiency and responsiveness.

Problems which the GMSS aims to solve: The project aimms to solve and reduce the dependancey on third party drivem open-source sytem scheduler
libraries, which may not always be optimized for specific use cases or may introduce compatibility issues and lack of essteail core fetures and
integration challenges which cause developers to not be able to maintain, expand and change the system scheduler according to their specific needs. 
By providing a generic and modular framework template, the GMSS allows developers to have more control over the scheduling process, enabling them to implement
custom scheduling policies and algorithms that are tailored to their application's requirements and reducing the need to pay for third party system scheduler and
reducing the need to build there own system scheuduler from the ground up.The Template ains to provide a foundation and starting piont for developers to build upon,
allowing them to focus on their specific scheduling needs without having to reinvent the wheel.

This project is Developer First focused and admin focused and it is up to the developer to implement the system scheduler and its features and functionality according to
their specific needs and requirements.This project is designed in a moduler acrhitecture , allowing developers to easily add or remove components as needed, and to customize
the system scheduler to meet their specific needs. The GMSS is designed to be flexible and adaptable, allowing developers to implement their own scheduling policies and algorithms,
and to integrate with other systems, applications and platforms as needed. The project is built to make to make integration easer by using a dedicated Extensibility and Integration Layer (EIL) 
which provides a standardized interface for integrating with other systems and applications, allowing developers to easily add new features and functionality to the system scheduler without 
having to modify the core codebase. The EIL also provides a framework for managing dependencies and ensuring that all components of the system scheduler are compatible with each other, 
reducing the risk of conflicts and errors.

What the project is not: The GMSS is not a all in one solution for all scheduling needs and only suports the core functionality and features of a system scheduler, and does not provide 
a complete solution for all scheduling needs. It is up to the developer to implement the specific features and functionality that they require for their application, and to ensure that
the system scheduler meets their specific needs and requirements. 

What Project supports: Windows , Linux, MacOS, and other operating systems but only windows is used for this build and the other builds for other platforms must be implemented by the
Developer themselves.GMSS has support for diffrent tupes of apllications webased and locally based but for the presentation only integrated mdoules and small directly integrated demo
applications are used for the demonstration of the project and its features and functionality. The GMSS is designed to be flexible and adaptable, allowing developers to 
implement their own scheduling policies and algorithms, and to integrate with other systems, applications, and platforms as needed.

What the Project utilizes: 
Three User Roles: Developer (Super Admin), Admin and Viewer.
The Scheduler and two demos each use there own database which is three databases in total.
Mdodules used : Monitoring agemt and inventory Managment Demon apllications , Login security module , UI module , Extensibility and core Scheduling Engine Module.
Readme Files which serve as a guide for developers to understand the project and its features and functionality, and to provide instructions for implementing the
system scheduler and its components.
